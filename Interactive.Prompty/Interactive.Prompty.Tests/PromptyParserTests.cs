namespace Interactive.Prompty.Tests;

public class PromptyParserTests
{
    [Fact]
    public void Parse_WhenGivenValidPrompty_ReturnsExpectedObject()
    {
        // Arrange
        var prompty = File.ReadAllText("basic.prompty");

        // Act
        var result = PromptyParser.Parse<PromptyMetadata>(prompty);
        var sample = result.Sample as IDictionary<string, object>;

        // Assert
        Assert.Equal("Seth", sample!["firstName"]);
    }
}
