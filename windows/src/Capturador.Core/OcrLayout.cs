namespace Capturador.Core;

public readonly record struct OcrWord(Rect2 Box, string Text);

/// <summary>
/// Monta o texto reconhecido em linhas, de cima para baixo, como no Mac. No modo tabela,
/// separa colunas com TAB, o que cola certo em planilhas.
/// </summary>
public static class OcrLayout
{
    public static string Arrange(IReadOnlyList<OcrWord> words, bool table)
    {
        if (words.Count == 0) return "";
        var rows = new List<List<OcrWord>>();
        foreach (var w in words.OrderBy(w => w.Box.Y + w.Box.Height / 2))
        {
            var row = rows.Count > 0 ? rows[^1] : null;
            if (row is not null && SameRow(row[0], w)) row.Add(w);
            else rows.Add([w]);
        }
        return string.Join("\n", rows.Select(row => JoinRow(row, table)));
    }

    private static bool SameRow(OcrWord first, OcrWord w)
    {
        double a = first.Box.Y + first.Box.Height / 2, b = w.Box.Y + w.Box.Height / 2;
        return Math.Abs(a - b) < Math.Min(first.Box.Height, w.Box.Height) * 0.5;
    }

    private static string JoinRow(List<OcrWord> row, bool table)
    {
        var sorted = row.OrderBy(w => w.Box.X).ToList();
        var parts = new System.Text.StringBuilder(sorted[0].Text);
        for (int i = 1; i < sorted.Count; i++)
        {
            var gap = sorted[i].Box.X - sorted[i - 1].Box.Right;
            var height = Math.Max(sorted[i].Box.Height, sorted[i - 1].Box.Height);
            // Um espaço maior que a altura da linha separa colunas.
            parts.Append(table && gap > height * 1.5 ? '\t' : ' ').Append(sorted[i].Text);
        }
        return parts.ToString();
    }
}
