using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PathWeb.Data;

namespace PathWeb.TagHelpers;

[HtmlTargetElement(Attributes = "field-help")]
public class FieldHelpTagHelper : TagHelper
{
    private readonly LabConfigContext _context;
    private readonly IMemoryCache _cache;
    private const string CacheKey = "FieldHelpDictionary";

    public FieldHelpTagHelper(LabConfigContext context, IMemoryCache cache)
    {
        _context = context;
        _cache = cache;
    }

    [HtmlAttributeName("field-help")]
    public string FieldName { get; set; } = null!;

    public override int Order => 10000;

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.Attributes.RemoveAll("field-help");

        var helpDictionary = await _cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
            return await _context.FieldHelps
                .AsNoTracking()
                .ToDictionaryAsync(f => f.FieldName, f => f.HelpText);
        });

        if (helpDictionary != null && helpDictionary.TryGetValue(FieldName, out var helpText))
        {
            var escapedText = System.Net.WebUtility.HtmlEncode(helpText);
            output.PostContent.AppendHtml(
                $" <span class=\"field-help-icon ms-1\" data-bs-toggle=\"popover\" data-bs-trigger=\"hover focus\" " +
                $"data-bs-content=\"{escapedText}\" tabindex=\"0\" role=\"button\" aria-label=\"Help\">" +
                $"<i class=\"bi bi-info-circle\"></i></span>");
        }
    }
}
