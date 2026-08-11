using System.ComponentModel.DataAnnotations;
using WorkshopOS.Web;
using WorkshopOS.Application.Catalog;

namespace WorkshopOS.Web.Models.Catalog;

public sealed class ServiceCatalogListViewModel
{
    public IReadOnlyList<ServiceCatalogRowViewModel> Services { get; set; } = Array.Empty<ServiceCatalogRowViewModel>();

    public string? Search { get; set; }

    public bool? IsActive { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalCount { get; set; }

    public bool CanManageCatalog { get; set; }
}

public sealed class ServiceCatalogRowViewModel
{
    public Guid ServiceCatalogItemId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public decimal DefaultUnitPrice { get; set; }

    public string CurrencyCode { get; set; } = string.Empty;

    public bool IsActive { get; set; }
}

public sealed class ServiceCatalogFormViewModel
{
    public Guid? ServiceCatalogItemId { get; set; }

    [Required]
    [MaxLength(CatalogInputValidator.MaxCodeLength)]
    [Display(Name = "Field_Code")]
    public string Code { get; set; } = string.Empty;

    [Required]
    [MaxLength(CatalogInputValidator.MaxNameLength)]
    [Display(Name = "Field_Name")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(CatalogInputValidator.MaxDescriptionLength)]
    [Display(Name = "Field_Description")]
    public string? Description { get; set; }

    [Required]
    [Display(Name = "Field_DefaultUnitPrice")]
    public decimal DefaultUnitPrice { get; set; }

    [Display(Name = "Field_Active")]
    public bool IsActive { get; set; } = true;
}

public sealed class PartCatalogListViewModel
{
    public IReadOnlyList<PartCatalogRowViewModel> Parts { get; set; } = Array.Empty<PartCatalogRowViewModel>();

    public string? Search { get; set; }

    public bool? IsActive { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalCount { get; set; }

    public bool CanManageCatalog { get; set; }
}

public sealed class PartCatalogRowViewModel
{
    public Guid PartCatalogItemId { get; set; }

    public string Sku { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public decimal DefaultUnitPrice { get; set; }

    public string CurrencyCode { get; set; } = string.Empty;

    public bool IsActive { get; set; }
}

public sealed class PartCatalogFormViewModel
{
    public Guid? PartCatalogItemId { get; set; }

    [Required]
    [MaxLength(CatalogInputValidator.MaxSkuLength)]
    [Display(Name = "Field_Sku")]
    public string Sku { get; set; } = string.Empty;

    [Required]
    [MaxLength(CatalogInputValidator.MaxNameLength)]
    [Display(Name = "Field_Name")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(CatalogInputValidator.MaxDescriptionLength)]
    [Display(Name = "Field_Description")]
    public string? Description { get; set; }

    [Required]
    [Display(Name = "Field_DefaultUnitPrice")]
    public decimal DefaultUnitPrice { get; set; }

    [Display(Name = "Field_Active")]
    public bool IsActive { get; set; } = true;
}

public sealed class CatalogPickerOptionViewModel
{
    public Guid Id { get; set; }

    public string Label { get; set; } = string.Empty;

    public decimal DefaultUnitPrice { get; set; }

    public string CurrencyCode { get; set; } = string.Empty;

    public decimal? StockOnHand { get; set; }
}
