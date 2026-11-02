namespace Infoman.Service
{
    /// <summary>
    /// Iisang lugar para sa lahat ng unit conversion ng raw materials.
    /// Base unit: g, mL, pcs (inventory at recipe).
    /// Cost unit: kg, L, pcs (lahat ng UnitPrice sa batch ay per cost unit).
    /// </summary>
    public static class UnitConverter
    {
        /// <summary>
        /// True kung ang base unit ay kailangang i-divide sa 1000 para maging cost unit.
        /// g -> kg, mL -> L. Ang pcs ay hindi nagbabago.
        /// </summary>
        public static bool NeedsCostUnitConversion(string? baseUnit)
            => baseUnit == "g" || baseUnit == "mL";

        /// <summary>
        /// Quantity sa base unit (g, mL, pcs) -> cost unit (kg, L, pcs).
        /// Gamitin ito kapag mag-mu-multiply sa UnitPrice.
        /// </summary>
        public static decimal ToCostUnitQty(decimal baseQty, string? baseUnit)
            => NeedsCostUnitConversion(baseUnit) ? baseQty / 1000m : baseQty;

        /// <summary>
        /// Quantity sa base unit (g, mL, pcs) -> display unit (kg, L, pcs).
        /// Kapareho ng ToCostUnitQty sa ngayon, pero hiwalay ang intent para madaling basahin.
        /// </summary>
        public static decimal ToDisplayQty(decimal baseQty, string? baseUnit)
            => ToCostUnitQty(baseQty, baseUnit);

        /// <summary>
        /// Convert ang recipe quantity mula sa unit na pinili (kg, g, L, mL, pcs)
        /// papunta sa base unit ng material (g, mL, pcs).
        /// </summary>
        public static decimal ToBaseUnit(decimal qty, string? fromUnit, string? baseUnit)
        {
            if (fromUnit == baseUnit) return qty;

            return (fromUnit, baseUnit) switch
            {
                ("kg", "g") => qty * 1000m,
                ("g", "kg") => qty / 1000m,
                ("L", "mL") => qty * 1000m,
                ("mL", "L") => qty / 1000m,
                _ => qty
            };
        }
    }
}