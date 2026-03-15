using System.Text;

namespace XamlToCSharpGenerator.Previewer.DesignerHost;

internal static class PreviewMarkupValueCodec
{
    public static string EncodeBase64Url(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static string DecodeBase64Url(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);

        var normalized = value
            .Replace('-', '+')
            .Replace('_', '/');
        switch (normalized.Length % 4)
        {
            case 2:
                normalized += "==";
                break;
            case 3:
                normalized += "=";
                break;
        }

        return Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
    }
}
