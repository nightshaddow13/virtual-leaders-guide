using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Resources.Annotations;

namespace VirtualLeadersGuide.Api.Data;

[Resource]
public class SmokeTestEntity : Identifiable<int>
{
    [Attr]
    public required string Name { get; set; }
}
