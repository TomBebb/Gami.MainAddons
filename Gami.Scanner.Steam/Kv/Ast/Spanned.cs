using System.Text;

namespace Gami.Scanner.Steam.Kv.Ast;

public readonly record struct Spanned<T>(T Value, int Line, int StartColumn, int EndColumn)
{
    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.Append(Value);
        builder.Append(": ");
        builder.Append(Line);
        builder.Append(':');
        builder.Append(StartColumn);
        if (EndColumn != StartColumn)
        {
            builder.Append(':');
            builder.Append(EndColumn);
        }

        return builder.ToString();
    }
}