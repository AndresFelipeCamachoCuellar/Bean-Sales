using Web.Models;

namespace Web.Models.ViewModels;

// ViewModel de la página pública "Orígenes" (Home/Origenes).
// Agrupa los productos VISIBLES (mismo criterio que Home/Index: no borrados,
// ProductStatus.Active y disponibles en el país seleccionado) por región de
// origen. La agrupación se hace EN MEMORIA tras materializar la consulta EF.
public class OrigenesViewModel
{
    public List<OriginRegionViewModel> Regions { get; set; } = new();

    // Totales calculados para la cabecera (sin cifras inventadas: son datos reales).
    public int TotalLots => Regions.Sum(r => r.LotCount);

    public int TotalFarms => Regions.Sum(r => r.Farms.Count);

    public int TotalRegions => Regions.Count;
}

// Una región de origen (Product.Origin). Los productos sin Origin caen en
// "Otros orígenes".
public class OriginRegionViewModel
{
    public string Name { get; set; } = "Otros orígenes";

    // Nº de lotes (productos) publicados en esta región.
    public int LotCount { get; set; }

    // Fincas distintas (Product.Farm) presentes en la región. Puede venir vacía.
    public List<string> Farms { get; set; } = new();

    // Rango de altura ya formateado, ej. "1.650 – 1.900 msnm". Null si ningún
    // producto de la región tiene Altitude parseable.
    public string? AltitudeRange { get; set; }

    // Productos de la región (para las tarjetas).
    public List<Product> Products { get; set; } = new();
}
