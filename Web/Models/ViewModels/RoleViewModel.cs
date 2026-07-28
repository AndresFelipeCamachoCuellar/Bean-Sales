using System.ComponentModel.DataAnnotations;

namespace Web.Models.ViewModels;

public class RoleViewModel
{
    public string Id { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Role Name")]
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
    
    // Optional: Count of users in this role?
    public int UserCount { get; set; }
}
