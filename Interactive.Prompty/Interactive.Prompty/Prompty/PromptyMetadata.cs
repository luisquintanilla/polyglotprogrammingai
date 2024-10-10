using YamlDotNet.Serialization;

public class PromptyMetadata
{
    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    [YamlMember(Alias = "description")]
    public string? Description { get; set; }

    [YamlMember(Alias = "version")]
    public string? Version { get; set; }

    [YamlMember(Alias = "tags")]
    public List<string>? Tags { get; set; }

    [YamlMember(Alias = "authors")]
    public List<string>? Authors { get; set; }

    [YamlMember(Alias = "inputs")]
    public Dictionary<string, object>? Inputs { get; set; }

    [YamlMember(Alias = "outputs")]
    public Dictionary<string, object>? Outputs { get; set; }

    [YamlMember(Alias = "sample")]
    public Dictionary<string, object>? Sample { get; set; }

    [YamlMember(Alias = "template")]
    public string? Template { get; set; } = "liquid";
}