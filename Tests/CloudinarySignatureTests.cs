using Web.Services.Media;
using Xunit;

namespace Tests;

/// <summary>
/// La parte criptográfica de la integración con Cloudinary, verificada contra el
/// CÓDIGO FUENTE del SDK oficial (jul-2026), que es la especificación ejecutable de la
/// receta que la documentación describe en prosa:
///
///   cloudinary_npm · lib/utils/index.js
///     · api_string_to_sign(params, signature_version = 2)
///         - descarta null/undefined/"" (clear_blank)
///         - une arreglos con coma
///         - ordena por clave y concatena "k1=v1&amp;k2=v2"
///         - v2 (default): escapa el "&amp;" literal como "%26" (anti parameter smuggling)
///     · api_sign_request(params, api_secret) = hash(to_sign + api_secret)
///     · lib/utils/consts.js → DEFAULT_SIGNATURE_ALGORITHM = "sha1"
///     · verify_api_response_signature(public_id, version) → firma {public_id, version}
///       SIEMPRE con signature_version 1
///
/// Si estos tests pasan, la firma que enviamos es correcta ANTES de tocar la red — el
/// mismo enfoque que se usó con WompiSignatureTests y los vectores de su documentación.
/// </summary>
public class CloudinarySignatureTests
{
    // Secreto de juguete: NO es una credencial real (aquí no puede haber secretos).
    private const string Secret = "abcd";

    // ------------------------------------------------------- Cadena a firmar

    [Fact]
    public void BuildStringToSign_OrdenaPorClaveYUneConAmpersand()
    {
        // A propósito en orden inverso: el resultado debe quedar alfabético.
        var cadena = CloudinarySignature.BuildStringToSign(new[]
        {
            new KeyValuePair<string, string?>("timestamp", "1315060510"),
            new KeyValuePair<string, string?>("public_id", "sample_image")
        });

        Assert.Equal("public_id=sample_image&timestamp=1315060510", cadena);
    }

    [Fact]
    public void BuildStringToSign_DescartaVaciosYNulos()
    {
        var cadena = CloudinarySignature.BuildStringToSign(new[]
        {
            new KeyValuePair<string, string?>("public_id", "foto"),
            new KeyValuePair<string, string?>("upload_preset", ""),      // vacío -> fuera
            new KeyValuePair<string, string?>("eager", null),            // null  -> fuera
            new KeyValuePair<string, string?>("timestamp", "100")
        });

        Assert.Equal("public_id=foto&timestamp=100", cadena);
    }

    [Fact]
    public void BuildStringToSign_ExcluyeLosParametrosQueNoSeFirman()
    {
        // file / cloud_name / resource_type / api_key / signature NUNCA entran en la firma:
        // van en la URL, en el multipart del archivo, o se añaden después de firmar.
        var cadena = CloudinarySignature.BuildStringToSign(new[]
        {
            new KeyValuePair<string, string?>("api_key", "123456789"),
            new KeyValuePair<string, string?>("cloud_name", "demo"),
            new KeyValuePair<string, string?>("file", "@foto.jpg"),
            new KeyValuePair<string, string?>("resource_type", "image"),
            new KeyValuePair<string, string?>("signature", "deadbeef"),
            new KeyValuePair<string, string?>("public_id", "foto"),
            new KeyValuePair<string, string?>("timestamp", "100")
        });

        Assert.Equal("public_id=foto&timestamp=100", cadena);
    }

    [Fact]
    public void BuildStringToSign_Version2EscapaElAmpersandLiteral()
    {
        // Protección contra "parameter smuggling": un valor con "&" no puede partirse
        // en dos parámetros. Es el comportamiento por defecto del SDK.
        var v2 = CloudinarySignature.BuildStringToSign(new[]
        {
            new KeyValuePair<string, string?>("context", "alt=uno&otro=dos")
        });

        Assert.Equal("context=alt=uno%26otro=dos", v2);

        var v1 = CloudinarySignature.BuildStringToSign(
            new[] { new KeyValuePair<string, string?>("context", "alt=uno&otro=dos") },
            signatureVersion: 1);

        Assert.Equal("context=alt=uno&otro=dos", v1);
    }

    [Fact]
    public void BuildStringToSign_SinAmpersandLasDosVersionesCoinciden()
    {
        // Nuestros parámetros reales (public_id, timestamp, allowed_formats,
        // upload_preset) nunca contienen "&": v1 y v2 producen lo mismo.
        var parametros = new[]
        {
            new KeyValuePair<string, string?>("public_id", "bean/products/abc/def"),
            new KeyValuePair<string, string?>("timestamp", "1690000000"),
            new KeyValuePair<string, string?>("allowed_formats", "jpg,jpeg,png,webp")
        };

        Assert.Equal(
            CloudinarySignature.BuildStringToSign(parametros, signatureVersion: 1),
            CloudinarySignature.BuildStringToSign(parametros, signatureVersion: 2));
    }

    // ------------------------------------------------------- Firma (SHA-1)

    [Fact]
    public void Sign_EsElSha1DeLaCadenaMasElSecreto()
    {
        // Vector reproducible: SHA-1("public_id=sample_image&timestamp=1315060510abcd").
        // Es exactamente lo que hace api_sign_request(to_sign + api_secret, "sha1").
        var esperado = CloudinarySignature.Sha1Hex("public_id=sample_image&timestamp=1315060510" + Secret);

        var firma = CloudinarySignature.Sign(new[]
        {
            new KeyValuePair<string, string?>("public_id", "sample_image"),
            new KeyValuePair<string, string?>("timestamp", "1315060510")
        }, Secret);

        Assert.Equal(esperado, firma);
        Assert.Equal("b4ad47fb4e25c7bf5f92a20089f9db59bc302313", firma);
    }

    [Fact]
    public void Sha1Hex_EsHexadecimalMinusculaDe40Caracteres()
    {
        var hash = CloudinarySignature.Sha1Hex("abc");

        Assert.Equal(40, hash.Length);
        Assert.Equal("a9993e364706816aba3e25717850c26c9cd0d89d", hash); // vector estándar de SHA-1
        Assert.Equal(hash.ToLowerInvariant(), hash);
    }

    [Fact]
    public void Sign_ExigeElApiSecret()
    {
        Assert.Throws<ArgumentException>(() => CloudinarySignature.Sign(
            new[] { new KeyValuePair<string, string?>("public_id", "x") }, ""));
    }

    [Fact]
    public void Sign_CambiaSiCambiaCualquierParametroFirmado()
    {
        var a = CloudinarySignature.Sign(new[]
        {
            new KeyValuePair<string, string?>("public_id", "bean/products/a/1"),
            new KeyValuePair<string, string?>("timestamp", "100")
        }, Secret);

        // Si el navegador intentara subir a la carpeta de otro producto, la firma no cuadra.
        var b = CloudinarySignature.Sign(new[]
        {
            new KeyValuePair<string, string?>("public_id", "bean/products/b/1"),
            new KeyValuePair<string, string?>("timestamp", "100")
        }, Secret);

        Assert.NotEqual(a, b);
    }

    // ------------------------------------------------- Firma de la respuesta

    [Fact]
    public void ResponseSignatureMatches_AceptaLaFirmaCalculadaConLaRecetaDelSdk()
    {
        // verify_api_response_signature: {public_id, version} con signature_version = 1.
        var publicId = "bean/products/6f9619ff8b86d011b42d00c04fc964ff/abc123";
        var version = 1690000000L;

        var firmaDeCloudinary = CloudinarySignature.Sha1Hex(
            $"public_id={publicId}&version={version}" + Secret);

        Assert.True(CloudinarySignature.ResponseSignatureMatches(publicId, version, firmaDeCloudinary, Secret));
    }

    [Fact]
    public void ResponseSignatureMatches_RechazaUnaFirmaAjenaOIncompleta()
    {
        var publicId = "bean/products/abc/def";

        Assert.False(CloudinarySignature.ResponseSignatureMatches(publicId, 1L, "no-es-hex", Secret));
        Assert.False(CloudinarySignature.ResponseSignatureMatches(publicId, 1L, null, Secret));
        Assert.False(CloudinarySignature.ResponseSignatureMatches(publicId, null, "aa", Secret));
        Assert.False(CloudinarySignature.ResponseSignatureMatches(null, 1L, "aa", Secret));
    }

    [Fact]
    public void HashesMatch_EsInsensibleAMayusculas()
    {
        Assert.True(CloudinarySignature.HashesMatch("aabbcc", "AABBCC"));
        Assert.False(CloudinarySignature.HashesMatch("aabbcc", "aabbcd"));
        Assert.False(CloudinarySignature.HashesMatch("aabbcc", "aabb"));
        Assert.False(CloudinarySignature.HashesMatch(null, "aabbcc"));
    }

    // --------------------------------------------------------- URLs de entrega

    [Fact]
    public void BuildDeliveryUrl_ArmaLaFormaDocumentada()
    {
        var url = CloudinarySignature.BuildDeliveryUrl(
            "demo", "bean/products/abc/def", ImageTransformations.Main, 1690000000L, "jpg");

        Assert.Equal(
            "https://res.cloudinary.com/demo/image/upload/c_fill,w_900,h_900,f_auto,q_auto/v1690000000/bean/products/abc/def.jpg",
            url);
    }

    [Fact]
    public void BuildDeliveryUrl_OmiteVersionYFormatoCuandoNoLosHay()
    {
        var url = CloudinarySignature.BuildDeliveryUrl("demo", "bean/products/abc/def");

        Assert.Equal("https://res.cloudinary.com/demo/image/upload/bean/products/abc/def", url);
    }

    [Fact]
    public void BuildDeliveryUrl_SinDatosDevuelveVacio()
    {
        Assert.Equal(string.Empty, CloudinarySignature.BuildDeliveryUrl("", "algo"));
        Assert.Equal(string.Empty, CloudinarySignature.BuildDeliveryUrl("demo", ""));
    }

    [Fact]
    public void BuildDeliveryUrl_ReproduceLaFormaCanonicaDelSecureUrl()
    {
        // SEGURIDAD: la URL que se persiste se DERIVA aquí en vez de copiar el secure_url
        // que reporta el navegador (una cadena arbitraria que luego se interpola en
        // atributos style/onclick). Este test fija la forma canónica que devuelve
        // Cloudinary, para que derivarla sea equivalente a confiar en él.
        var url = CloudinarySignature.BuildDeliveryUrl(
            cloudName: "demo",
            publicId: "bean/products/6f9619ff8b86d011b42d00c04fc964ff/abc123",
            transformation: null,
            version: 1690000000L,
            format: "webp");

        Assert.Equal(
            "https://res.cloudinary.com/demo/image/upload/v1690000000/bean/products/6f9619ff8b86d011b42d00c04fc964ff/abc123.webp",
            url);

        // Y de esa base salen todas las variantes sin volver a tocar credenciales.
        Assert.Equal(
            "https://res.cloudinary.com/demo/image/upload/c_fill,w_640,h_420,f_auto,q_auto/v1690000000/bean/products/6f9619ff8b86d011b42d00c04fc964ff/abc123.webp",
            CloudinarySignature.WithTransformation(url, ImageTransformations.Catalog));
    }

    [Fact]
    public void WithTransformation_InsertaLaTransformacionDespuesDeUpload()
    {
        var secureUrl = "https://res.cloudinary.com/demo/image/upload/v1690000000/bean/products/abc/def.jpg";

        var conThumb = CloudinarySignature.WithTransformation(secureUrl, ImageTransformations.Thumb);

        Assert.Equal(
            "https://res.cloudinary.com/demo/image/upload/c_fill,w_148,h_148,f_auto,q_auto/v1690000000/bean/products/abc/def.jpg",
            conThumb);
    }

    [Fact]
    public void WithTransformation_EsIdempotente()
    {
        var secureUrl = "https://res.cloudinary.com/demo/image/upload/v1/bean/x.jpg";

        var una = CloudinarySignature.WithTransformation(secureUrl, ImageTransformations.Catalog);
        var dos = CloudinarySignature.WithTransformation(una, ImageTransformations.Catalog);

        Assert.Equal(una, dos);
    }

    [Fact]
    public void WithTransformation_FuerzaHttps()
    {
        var http = "http://res.cloudinary.com/demo/image/upload/v1/bean/x.jpg";

        var url = CloudinarySignature.WithTransformation(http, ImageTransformations.Catalog);

        Assert.StartsWith("https://res.cloudinary.com/", url);
    }

    [Fact]
    public void WithTransformation_DejaIntactaUnaUrlAjena()
    {
        // Compatibilidad: una URL externa pegada a mano en Product.ImageUrl sigue sirviendo.
        const string externa = "https://mi-finca.com/fotos/lote.jpg";

        Assert.Equal(externa, CloudinarySignature.WithTransformation(externa, ImageTransformations.Catalog));
        Assert.Null(CloudinarySignature.WithTransformation(null, ImageTransformations.Catalog));
        Assert.Equal(externa, CloudinarySignature.WithTransformation(externa, null));
    }

    // ------------------------------------------------------------- Opciones

    [Fact]
    public void CloudinaryOptions_SinCredencialesNoPermiteSubir()
    {
        // Degradación segura: es lo que hace que el sitio funcione igual que antes.
        Assert.False(new CloudinaryOptions().CanUpload);

        Assert.False(new CloudinaryOptions
        {
            CloudName = "demo",
            ApiKey = "123",
            ApiSecret = "PEGA_AQUI_EL_SECRETO"   // marcador de la plantilla: no cuenta
        }.CanUpload);

        Assert.False(new CloudinaryOptions
        {
            Enabled = false,
            CloudName = "demo",
            ApiKey = "123",
            ApiSecret = "s3cr3t"
        }.CanUpload);

        Assert.True(new CloudinaryOptions
        {
            CloudName = "demo",
            ApiKey = "123",
            ApiSecret = "s3cr3t"
        }.CanUpload);
    }

    [Fact]
    public void CloudinaryOptions_NormalizaFormatosYCarpeta()
    {
        var options = new CloudinaryOptions
        {
            Folder = "/bean/products/",
            AllowedFormats = new[] { ".JPG", "jpeg", "png", "png", " webp " }
        };

        Assert.Equal("bean/products", options.FolderOrDefault);
        Assert.Equal("jpg,jpeg,png,webp", options.AllowedFormatsCsv);

        var productId = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");
        Assert.Equal("bean/products/6f9619ff8b86d011b42d00c04fc964ff", options.FolderFor(productId));
    }

    [Fact]
    public void CloudinaryOptions_ArmaLosEndpointsDelApi()
    {
        var options = new CloudinaryOptions { CloudName = "demo" };

        Assert.Equal("https://api.cloudinary.com/v1_1/demo/image/upload", options.UploadUrl);
        Assert.Equal("https://api.cloudinary.com/v1_1/demo/image/destroy", options.DestroyUrl);
    }
}
