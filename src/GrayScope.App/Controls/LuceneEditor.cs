using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using GrayScope.Core.Querying;

namespace GrayScope.App.Controls;

/// <summary>
/// Lucene query editor: syntax highlighting, field/operator completion (typing or Ctrl+Space)
/// and a bindable <see cref="Code"/> property. Ctrl+Enter is left to the window's key bindings.
/// </summary>
public sealed class LuceneEditor : TextEditor
{
    public static readonly StyledProperty<string?> CodeProperty =
        AvaloniaProperty.Register<LuceneEditor, string?>(nameof(Code), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<IReadOnlyList<string>?> CompletionFieldsProperty =
        AvaloniaProperty.Register<LuceneEditor, IReadOnlyList<string>?>(nameof(CompletionFields));

    private static readonly string[] Operators = ["AND", "OR", "NOT", "TO", "_exists_:"];
    private const int MaxCompletionItems = 200;

    private CompletionWindow? _completion;
    private bool _syncing;

    public LuceneEditor()
    {
        FontFamily = Application.Current?.FindResource("MonoFont") as FontFamily ?? FontFamily.Default;
        FontSize = 12.5;
        WordWrap = true;
        ShowLineNumbers = false;
        Options.EnableHyperlinks = false;
        Options.EnableEmailHyperlinks = false;
        Options.ConvertTabsToSpaces = true;
        TextArea.TextView.LineTransformers.Add(new LuceneColorizer(this));
        TextArea.TextEntered += OnTextEntered;
        TextArea.KeyDown += OnEditorKeyDown;
        ActualThemeVariantChanged += (_, _) => TextArea.TextView.Redraw();
    }

    // TextEditor's control theme must still apply to this subclass.
    protected override Type StyleKeyOverride => typeof(TextEditor);

    public string? Code
    {
        get => GetValue(CodeProperty);
        set => SetValue(CodeProperty, value);
    }

    public IReadOnlyList<string>? CompletionFields
    {
        get => GetValue(CompletionFieldsProperty);
        set => SetValue(CompletionFieldsProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CodeProperty && !_syncing)
        {
            var value = change.GetNewValue<string?>() ?? "";
            if (value != Text)
            {
                _syncing = true;
                Document.Text = value;
                CaretOffset = value.Length;
                _syncing = false;
            }
        }
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        if (!_syncing)
        {
            _syncing = true;
            SetCurrentValue(CodeProperty, Text);
            _syncing = false;
        }
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            ShowCompletion(force: true);
            e.Handled = true;
        }
    }

    private void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        if (_completion is null && e.Text is { Length: 1 } typed && LuceneTokenizer.IsWordChar(typed[0]) && typed[0] != '-')
        {
            ShowCompletion(force: false);
        }
    }

    private void ShowCompletion(bool force)
    {
        var text = Text;
        var caret = CaretOffset;
        var prefix = LuceneTokenizer.WordBefore(text, caret);
        if (!force && prefix.Length < 2)
        {
            return;
        }

        var candidates = (CompletionFields ?? [])
            .Select(f => new LuceneCompletion(f + ":", f, "alan"))
            .Concat(Operators.Select(o => new LuceneCompletion(o.EndsWith(':') ? o : o + " ", o, "operatör")))
            .Where(c => prefix.Length == 0 || c.Label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Take(MaxCompletionItems)
            .ToList();
        if (candidates.Count == 0)
        {
            return;
        }

        _completion = new CompletionWindow(TextArea) { StartOffset = caret - prefix.Length, CloseWhenCaretAtBeginning = true };
        foreach (var candidate in candidates)
        {
            _completion.CompletionList.CompletionData.Add(candidate);
        }
        _completion.Closed += (_, _) => _completion = null;
        _completion.Show();
        if (prefix.Length > 0)
        {
            _completion.CompletionList.SelectItem(prefix);
        }
    }

    internal IBrush? Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : null;

    private sealed class LuceneCompletion(string insertText, string label, string kind) : ICompletionData
    {
        public string Label { get; } = label;
        public Avalonia.Media.IImage? Image => null;
        public string Text => Label;
        public object Content => Label;
        public object Description => kind;
        public double Priority => kind == "alan" ? 1 : 0;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs) =>
            textArea.Document.Replace(completionSegment, insertText);
    }

    /// <summary>Colours tokens per line using theme brushes (re-resolved on every redraw, so theme switches apply).</summary>
    private sealed class LuceneColorizer(LuceneEditor owner) : DocumentColorizingTransformer
    {
        protected override void ColorizeLine(DocumentLine line)
        {
            var text = CurrentContext.Document.GetText(line);
            foreach (var token in LuceneTokenizer.Tokenize(text))
            {
                var key = token.Kind switch
                {
                    LuceneTokenKind.Field => "SyntaxFieldBrush",
                    LuceneTokenKind.Operator => "SyntaxOperatorBrush",
                    LuceneTokenKind.Quoted => "SyntaxStringBrush",
                    LuceneTokenKind.Parameter => "SyntaxParameterBrush",
                    LuceneTokenKind.Range => "SyntaxRangeBrush",
                    LuceneTokenKind.Number or LuceneTokenKind.Wildcard => "SyntaxNumberBrush",
                    _ => null,
                };
                if (key is null || owner.Brush(key) is not { } brush)
                {
                    continue;
                }
                var isBold = token.Kind is LuceneTokenKind.Operator or LuceneTokenKind.Parameter;
                ChangeLinePart(line.Offset + token.Start, line.Offset + token.Start + token.Length, element =>
                {
                    element.TextRunProperties.SetForegroundBrush(brush);
                    if (isBold)
                    {
                        var tf = element.TextRunProperties.Typeface;
                        element.TextRunProperties.SetTypeface(new Typeface(tf.FontFamily, tf.Style, FontWeight.Bold));
                    }
                });
            }
        }
    }
}
