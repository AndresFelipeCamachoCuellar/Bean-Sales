namespace Web.Models.Enums;

/// <summary>
/// Quién subió la foto. En un modelo de consignación el proveedor es un TERCERO:
/// saber si la foto la puso él o el equipo de Bean es información de auditoría real
/// (por ejemplo, para reclamar si la foto no corresponde al lote que llegó a bodega).
/// </summary>
public enum ImageUploader
{
    /// <summary>La subió el proveedor desde su panel (lote en Borrador o Rechazado).</summary>
    Provider = 0,

    /// <summary>La subió el equipo de Bean (staff con permiso ProductApprovals/Update).</summary>
    Admin = 1
}
