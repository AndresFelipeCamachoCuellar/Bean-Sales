using System.ComponentModel.DataAnnotations;

namespace Web.Models.ViewModels;

public class UserViewModel
{
    public string Id { get; set; } = string.Empty;
    
    [Display(Name = "Usuario")]
    public string UserName { get; set; } = string.Empty;
    
    [Display(Name = "Nombre Completo")]
    public string FullName { get; set; } = string.Empty;
    
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;
    
    [Display(Name = "Documento")]
    public string DocumentNumber { get; set; } = string.Empty;
    
    [Display(Name = "Estado")]
    public bool Status { get; set; }
    
    [Display(Name = "Roles")]
    public IEnumerable<string> Roles { get; set; } = new List<string>();
}
