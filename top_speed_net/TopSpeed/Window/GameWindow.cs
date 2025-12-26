using System.Drawing;
using System.Windows.Forms;

namespace TopSpeed.Windowing
{
    internal sealed class GameWindow : Form
    {
        private readonly TextBox _inputBox;
        private bool _submitPending;
        private bool _cancelPending;
        private string _submittedText = string.Empty;

        public GameWindow()
        {
            Text = "Top Speed";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = true;
            ClientSize = new Size(640, 360);
            KeyPreview = true;

            _inputBox = new TextBox
            {
                Visible = false,
                Enabled = false,
                AcceptsReturn = true,
                CausesValidation = false,
                ImeMode = ImeMode.NoControl,
                TabStop = false,
                BorderStyle = BorderStyle.FixedSingle,
                Width = 400,
                Left = 12,
                Top = ClientSize.Height - 48,
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };
            _inputBox.KeyDown += OnInputKeyDown;
            Controls.Add(_inputBox);
        }

        public void ShowTextInput(string? initialText)
        {
            _submittedText = string.Empty;
            _submitPending = false;
            _cancelPending = false;
            _inputBox.Text = initialText ?? string.Empty;
            _inputBox.Enabled = true;
            _inputBox.Visible = true;
            _inputBox.Focus();
            _inputBox.SelectAll();
        }

        public void HideTextInput()
        {
            _inputBox.Visible = false;
            _inputBox.Enabled = false;
            Focus();
        }

        public bool TryConsumeTextInput(out TextInputResult result)
        {
            if (_submitPending)
            {
                _submitPending = false;
                result = TextInputResult.Submitted(_submittedText);
                return true;
            }
            if (_cancelPending)
            {
                _cancelPending = false;
                result = TextInputResult.CreateCancelled();
                return true;
            }
            result = default;
            return false;
        }

        private void OnInputKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                _submittedText = _inputBox.Text;
                _submitPending = true;
                HideTextInput();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                _cancelPending = true;
                HideTextInput();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }
    }

    internal readonly struct TextInputResult
    {
        private TextInputResult(bool cancelled, string text)
        {
            Cancelled = cancelled;
            Text = text;
        }

        public bool Cancelled { get; }
        public string Text { get; }

        public static TextInputResult Submitted(string text) => new TextInputResult(false, text ?? string.Empty);

        public static TextInputResult CreateCancelled() => new TextInputResult(true, string.Empty);
    }
}
