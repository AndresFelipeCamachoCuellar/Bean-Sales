using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Web.Models;

public class ShoppingCartItem
{
    [Key]
    public Guid ShoppingCartItemID { get; set; }

    // For Authenticated Users
    public Guid? UserID { get; set; }
    [ForeignKey("UserID")]
    public virtual ApplicationUser? User { get; set; }

    // For Anonymous Users (Session) - though we might not save to DB?
    // User requested "if client logs in they can see what is in the shopping cart".
    // Strategy: 
    // - Anonymous: Keep in Session (JSON).
    // - Authenticated: Keep in DB.
    // - Login: Convert Session -> DB.
    // So this Entity is primarly for Persistent Cart.

    public Guid ProductID { get; set; }
    [ForeignKey("ProductID")]
    public virtual Product? Product { get; set; }

    public int Quantity { get; set; }
    
    public DateTime CreatedOn { get; set; }
}
