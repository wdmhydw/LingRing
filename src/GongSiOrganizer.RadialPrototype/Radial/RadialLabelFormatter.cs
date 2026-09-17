using System.Globalization;

namespace GongSiOrganizer.RadialPrototype.Radial;

internal static class RadialLabelFormatter
{
    public static RadialCellLabel Layout(string name, RadialCellContent space, Func<string, double> measure)
    {
        RadialCellLabel Place(string text, System.Windows.Rect bounds)
        {
            if (bounds.IsEmpty) return new("", bounds, true);
            var height = (text.Count(c => c == '\n') + 1) * RadialTypography.LineHeight;
            return new(text, new System.Windows.Rect(bounds.X, space.Center.Y - height / 2, bounds.Width, height),
                text.Replace("\n", "", StringComparison.Ordinal) != name);
        }
        var single = space.SingleLine;
        if (!single.IsEmpty && measure(name) <= single.Width) return Place(name, single);
        var oneLine = single.IsEmpty ? "" : OneLine(name, single.Width, measure);
        var two = space.TwoLines;
        if (!two.IsEmpty)
        {
            var twoLines = TwoLines(name, two.Width, measure);
            static int VisibleLetters(string text) => new StringInfo(text.Replace("\n", "").Replace("…", "")).LengthInTextElements;
            if (VisibleLetters(twoLines) > VisibleLetters(oneLine)) return Place(twoLines, two);
        }
        return Place(oneLine, single);
    }

    public static string OneLine(string name, double width, Func<string, double> measure)
    {
        if (measure(name) <= width) return name;
        var result = "";
        var iterator = StringInfo.GetTextElementEnumerator(name);
        while (iterator.MoveNext() && measure(result + iterator.GetTextElement() + "…") <= width)
            result += iterator.GetTextElement();
        return result + "…";
    }
    // Measure in the actual font. Keep the disambiguating suffix and whole
    // grapheme clusters; never split a surrogate pair or a combining sequence.
    public static string TwoLines(string name, double width, Func<string, double> measure)
    {
        if (measure(name) <= width) return name;
        var elements = new List<string>();
        var iterator = StringInfo.GetTextElementEnumerator(name);
        while (iterator.MoveNext()) elements.Add(iterator.GetTextElement());
        var first = "";
        var used = 0;
        while (used < elements.Count && measure(first + elements[used]) <= width)
            first += elements[used++];
        var remaining = string.Concat(elements.Skip(used));
        if (measure(remaining) <= width)
        {
            // Avoid leaving one character alone on the second line when the
            // complete name fits in two lines.
            var minimumTail = Math.Min(2, elements.Count / 2);
            while (used > 1 && elements.Count - used < minimumTail &&
                   measure(string.Concat(elements.Skip(used - 1))) <= width)
                used--;
            return string.Concat(elements.Take(used)) + "\n" + string.Concat(elements.Skip(used));
        }
        var tail = "";
        for (var i = elements.Count - 1; i >= used && measure("…" + elements[i] + tail) <= width; i--)
            tail = elements[i] + tail;
        return first + "\n…" + tail;
    }
}
