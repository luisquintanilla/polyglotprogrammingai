using Markdig;
using Markdig.Extensions.Yaml;
using Markdig.Syntax;
using YamlDotNet.Serialization;

internal class PromptyParser
{
    public static T Parse<T>(string prompty)
    {
        var yamlDeserializer = 
            new DeserializerBuilder()
            .IgnoreUnmatchedProperties()
            .Build();

        var pipeline = 
            new MarkdownPipelineBuilder()
            .UseYamlFrontMatter()
            .Build();    

        var document = Markdown.Parse(prompty, pipeline);

        var block = 
            document
                .Descendants<YamlFrontMatterBlock>()
                .FirstOrDefault();

        if (block == null) return default;

        var yaml = 
            block
                .Lines
                .Lines
                .OrderByDescending(x => x.Line)
                .Select(x => $"{x}\n")
                .ToList()
                .Select(x => x.Replace("---", string.Empty))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Aggregate((s,agg) => agg + s);  
        
        return yamlDeserializer.Deserialize<T>(yaml);
    }
}