using ManagedCode.ClaudeCodeSharpSDK.Models;

namespace ManagedCode.ClaudeCodeSharpSDK.Tests.Unit;

public class StructuredOutputSchemaTests
{
    private const string AdditionalPropertiesPropertyName = "additionalProperties";
    private const string BlankPropertyName = " ";
    private const string BooleanSchemaType = "boolean";
    private const string MustExistInSchemaPropertiesMessageFragment = "must exist in schema properties";
    private const string NumberSchemaType = "number";
    private const string ObjectSchemaType = "object";
    private const string PropertiesPropertyName = "properties";
    private const string RequiredPropertyName = "required";
    private const string TypePropertyName = "type";
    private const string StringSchemaType = "string";

    private sealed record ScoreStatusResponse(string Status, double Score, bool Ok);
    private sealed record MissingPropertyResponse(string Missing);

    [Test]
    public async Task Object_BuildsExpectedSchema()
    {
        var schema = StructuredOutputSchema.Map<ScoreStatusResponse>(
            additionalProperties: false,
            (response => response.Status, StructuredOutputSchema.PlainText()),
            (response => response.Score, StructuredOutputSchema.Numeric()),
            (response => response.Ok, StructuredOutputSchema.Flag()));

        var json = schema.ToJsonObject();
        await Assert.That(json[TypePropertyName]!.GetValue<string>()).IsEqualTo(ObjectSchemaType);
        await Assert.That(json[PropertiesPropertyName]![nameof(ScoreStatusResponse.Status)]![TypePropertyName]!.GetValue<string>()).IsEqualTo(StringSchemaType);
        await Assert.That(json[PropertiesPropertyName]![nameof(ScoreStatusResponse.Score)]![TypePropertyName]!.GetValue<string>()).IsEqualTo(NumberSchemaType);
        await Assert.That(json[PropertiesPropertyName]![nameof(ScoreStatusResponse.Ok)]![TypePropertyName]!.GetValue<string>()).IsEqualTo(BooleanSchemaType);
        await Assert.That(json[RequiredPropertyName]![0]!.GetValue<string>()).IsEqualTo(nameof(ScoreStatusResponse.Status));
        await Assert.That(json[AdditionalPropertiesPropertyName]!.GetValue<bool>()).IsFalse();
    }

    [Test]
    public async Task Object_ThrowsForInvalidPropertyName()
    {
        var action = () => StructuredOutputSchema.Map(
            new Dictionary<string, StructuredOutputSchema>
            {
                [BlankPropertyName] = StructuredOutputSchema.PlainText(),
            });

        var exception = await Assert.That(action).ThrowsException();
        await Assert.That(exception).IsTypeOf<ArgumentException>();
    }

    [Test]
    public async Task Object_ThrowsWhenRequiredPropertyMissingFromProperties()
    {
        var action = () => StructuredOutputSchema.Map(
            new Dictionary<string, StructuredOutputSchema>
            {
                [nameof(ScoreStatusResponse.Status)] = StructuredOutputSchema.PlainText(),
            },
            required: [nameof(MissingPropertyResponse.Missing)]);

        var exception = await Assert.That(action).ThrowsException();
        await Assert.That(exception).IsTypeOf<ArgumentException>();
        await Assert.That(exception!.Message).Contains(MustExistInSchemaPropertiesMessageFragment);
    }

    [Test]
    public async Task Object_ThrowsForUnsupportedPropertySelector()
    {
        var action = () => StructuredOutputSchema.Map<ScoreStatusResponse>(
            additionalProperties: false,
            (response => response.Status.Trim(), StructuredOutputSchema.PlainText()));

        var exception = await Assert.That(action).ThrowsException();
        await Assert.That(exception).IsTypeOf<ArgumentException>();
    }
}
