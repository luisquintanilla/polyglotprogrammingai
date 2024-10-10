using YamlDotNet.Serialization;

public class PromptyMetadata
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; }
    
    [YamlMember(Alias = "description")]
    public string Description { get; set; }

    [YamlMember(Alias = "version")]
    public string Version { get; set; }

    [YamlMember(Alias = "authors")]
    public string[] Authors { get; set; }
}