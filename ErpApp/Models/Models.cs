namespace ErpApp.Models;

public class ComboItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public override string ToString() => Name;
}

public class InvoiceLine
{
    public int ItemId { get; set; }
    public string ItemName { get; set; } = "";
    public string Model { get; set; } = "";
    public string SideSize { get; set; } = "";
    public string Brand { get; set; } = "";
    public string Uom { get; set; } = "";
    public decimal Qty { get; set; }
    public decimal Rate { get; set; }
    public decimal DiscPercent { get; set; }
    public decimal Amount => Math.Round(Qty * Rate * (1 - DiscPercent / 100m), 2);
}
