using Microsoft.Extensions.Options;

namespace Web.Services.Media;

/// <summary>
/// Degradación segura: es lo que se registra cuando NO hay credenciales de Cloudinary.
/// Equivalente de <c>FixedShippingQuoteService</c> en el envío y de
/// <c>WompiOptions.CanCharge == false</c> en los pagos: el sitio abre, el catálogo
/// funciona, los formularios de producto guardan, y el gestor de fotos muestra un aviso
/// de "no configurado" en lugar del dropzone. Sin excepciones en el log.
/// </summary>
public class DisabledImageStorage : IProductImageStorage
{
    private readonly CloudinaryOptions _options;

    public DisabledImageStorage(IOptions<CloudinaryOptions> options)
    {
        _options = options.Value;
    }

    public bool CanUpload => false;

    public int MaxImagesPerProduct => _options.MaxImagesOrDefault;

    public long MaxFileSizeBytes => _options.MaxFileSizeOrDefault;

    public int MinDimensionPx => _options.MinDimensionOrDefault;

    public IReadOnlyList<string> AllowedFormats => _options.NormalizedFormats;

    public string AcceptAttribute => _options.AcceptAttribute;

    /// <summary>
    /// Nunca se llama: los endpoints cortan antes con <see cref="CanUpload"/>. Si algún
    /// día alguien lo llama, es un bug de programación y debe verse fuerte.
    /// </summary>
    public UploadTicket CreateUploadTicket(Guid productId) =>
        throw new InvalidOperationException(
            "La galería de fotos no está configurada (faltan Cloudinary:CloudName/ApiKey/ApiSecret).");

    public bool BelongsToProduct(Guid productId, string? publicId) => false;

    public bool ResponseSignatureIsValid(CloudinaryUploadResult result) => false;

    /// <summary>
    /// No-op exitoso: sin credenciales no hay nada que borrar en la nube, y devolver
    /// false haría que el servicio de aplicación registrara advertencias inútiles.
    /// </summary>
    public Task<bool> DeleteAsync(string publicId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}
