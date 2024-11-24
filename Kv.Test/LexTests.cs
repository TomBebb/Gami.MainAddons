using System.Collections.Immutable;
using System.Text;
using Gami.Scanner.Steam.Kv;
using Gami.Scanner.Steam.Kv.Ast;

namespace Kv.Test;

public class LexTests
{
    private static ValueTask<ImmutableArray<Spanned<Token>>> LexAll(string text)
    {
        var textStream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        using var streamReader = new StreamReader(textStream);
        return LexAll(streamReader);
    }

    private static async ValueTask<ImmutableArray<Spanned<Token>>> LexAll(StreamReader s)
    {
        var tokens = ImmutableArray.CreateBuilder<Spanned<Token>>();
        await foreach (var token in s.LexKv()) tokens.Add(token);

        return tokens.ToImmutable();
    }

    private static async ValueTask<Spanned<Token>> LexSingle(string input)
    {
        var textStream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        var lexer = textStream.LexKv().GetAsyncEnumerator();
        if (!await lexer.MoveNextAsync()) throw new Exception("Expected single token, got empty");
        var val = lexer.Current;
        if (await lexer.MoveNextAsync()) throw new Exception("Expected single token, got multiple");
        return val;
    }

    [Test]
    public async ValueTask TestJustText()
    {
        const string input = "\"Demo\"";
        var res = await LexSingle(input);
        Assert.That(res,
            Is.EqualTo(new Spanned<Token>(new Token(TokenType.String, "Demo"), 1, 1, 7)));
    }

    [Test]
    public async ValueTask TestJustStartObject()
    {
        const string input = "{";
        var res = await LexSingle(input);
        Assert.That(res,
            Is.EqualTo(new Spanned<Token>(new Token(TokenType.StartObject), 1, 1, 1)));
    }

    [Test]
    public async ValueTask TestJustEndObject()
    {
        const string input = "}";
        var res = await LexSingle(input);
        Assert.That(res,
            Is.EqualTo(new Spanned<Token>(new Token(TokenType.EndObject), 1, 1, 1)));
    }

    [Test]
    public async ValueTask TestJustObject()
    {
        const string input = "{}";
        var res = await LexAll(input);
        Assert.That(res, Is.EqualTo(new[]
        {
            new Spanned<Token>(new Token(TokenType.StartObject), 1, 1, 1),
            new Spanned<Token>(new Token(TokenType.EndObject), 1, 2, 2)
        }));
    }

    [Test]
    public async ValueTask TestOffsetStartObject()
    {
        const string input = "   {";
        var res = await LexSingle(input);
        Assert.That(res, Is.EqualTo(
            new Spanned<Token>(new Token(TokenType.StartObject), 1, 4, 4)));
    }
}