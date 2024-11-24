using System.Text;
using Gami.Scanner.Steam.Kv.Ast;

namespace Gami.Scanner.Steam.Kv;

public class InvalidCharLexError(char ch, int line, int column) : Exception
{
    public override string Message => $"Invalid character {ch} in at {line}:{column}";
}

public static class Lexer
{
    private record struct LexTextState(int Line, int Column, StringBuilder Text);


    public static IAsyncEnumerable<Spanned<Token>> LexKv(this Stream s) => new StreamReader(s).LexKv();

    public static async IAsyncEnumerable<Spanned<Token>> LexKv(this StreamReader sr)
    {
        var buffer = new Memory<char>(new char[1024]);
        int charsRead, line = 1, column = 1;

        Spanned<Token> AutoPos(Token token) => new(token, line, column, column);
        Spanned<Token> AutoPosBasic(TokenType token) => AutoPos(new Token(token));

        LexTextState? textState = null;
        do
        {
            charsRead = await sr.ReadAsync(buffer);
            for (var i = 0; i < charsRead; i++)
            {
                var ch = buffer.Span[i];
                if (textState != null)
                {
                    var state = textState.Value;
                    if (ch == '"')
                        yield return new Spanned<Token>(new Token(TokenType.String, state.Text.ToString()),
                            state.Line,
                            state.Column, state.Column + 2 + state.Text.Length);
                    else
                        state.Text.Append(ch);


                    column++;
                    continue;
                }

                switch (ch)
                {
                    case '{':
                        yield return AutoPosBasic(TokenType.StartObject);
                        break;
                    case '}':
                        yield return AutoPosBasic(TokenType.EndObject);
                        break;
                    case '"':
                        textState = new LexTextState(line, column, new StringBuilder());
                        break;
                    // skip whitespace
                    case ' ':
                    case '\t':
                    case '\r':
                        break;
                    case '\n':
                        line++;
                        break;
                    default: throw new InvalidCharLexError(ch, line, column);
                }

                column++;
            }
        } while (charsRead == buffer.Length);
    }
}