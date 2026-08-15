using System.Text;

namespace ErpApp.Data;

public static class NumberToWords
{
    private static readonly string[] Ones =
    {
        "Zero","One","Two","Three","Four","Five","Six","Seven","Eight","Nine","Ten",
        "Eleven","Twelve","Thirteen","Fourteen","Fifteen","Sixteen","Seventeen","Eighteen","Nineteen"
    };
    private static readonly string[] Tens =
    {
        "", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety"
    };

    public static string Convert(decimal amount)
    {
        long whole = (long)Math.Floor(amount);
        if (whole == 0) return "Zero Only";

        var sb = new StringBuilder();
        sb.Append(ConvertWhole(whole));
        sb.Append(" Only");
        return sb.ToString().Replace("  ", " ").Trim();
    }

    private static string ConvertWhole(long n)
    {
        if (n == 0) return "";
        if (n < 20) return Ones[n];
        if (n < 100) return Tens[n / 10] + (n % 10 != 0 ? " " + Ones[n % 10] : "");
        if (n < 1000) return Ones[n / 100] + " Hundred" + (n % 100 != 0 ? " " + ConvertWhole(n % 100) : "");
        if (n < 100000) return ConvertWhole(n / 1000) + " Thousand" + (n % 1000 != 0 ? " " + ConvertWhole(n % 1000) : "");
        if (n < 10000000) return ConvertWhole(n / 100000) + " Lac" + (n % 100000 != 0 ? " " + ConvertWhole(n % 100000) : "");
        return ConvertWhole(n / 10000000) + " Crore" + (n % 10000000 != 0 ? " " + ConvertWhole(n % 10000000) : "");
    }
}
