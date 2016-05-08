using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

using System.Diagnostics;
using Windows.UI;
using Windows.UI.Xaml.Documents;
using Windows.UI.Text.Core;
using Microsoft.Graphics.Canvas.Text;
using Windows.ApplicationModel.Core;
using Windows.UI.Core;
using Windows.UI.Text;
using Windows.System;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel;
using System.Threading.Tasks;


// The User Control item template is documented at http://go.microsoft.com/fwlink/?LinkId=234236

namespace MyEdit {
    public sealed partial class MyEditor : UserControl {
        const char LF = '\n';
        int CursorPos = 0;
        List<TChar> Chars = new List<TChar>();
        CanvasTextFormat TextFormat = new CanvasTextFormat();
        bool InComposition = false;
        int SelOrigin = -1;
        int SelCurrent = -1;
        int SelStart;
        int SelEnd;
        int LineCount = 1;
        double LineHeight = double.NaN;
        int ViewLineCount;
        Point ViewPadding = new Point(5, 5);
        Point ClickedPoint = new Point(double.NaN, double.NaN);

        // テキストの選択位置
        CoreTextRange Selection;

        /*
            コンストラクタ
        */
        public MyEditor() {
            this.InitializeComponent();

            // テキストの選択位置を初期化します。
            Selection.StartCaretPosition = 0;
            Selection.EndCaretPosition = 0;

            //TextFormat.FontFamily = "ＭＳ ゴシック";
        }

        /*
            コントロールがロードされた。
        */
        private void UserControl_Loaded(object sender, RoutedEventArgs e) {
            Debug.WriteLine("<<--- Control Loaded");
            CoreWindow wnd = CoreApplication.GetCurrentView().CoreWindow;

            // イベントハンドラを登録します。
            wnd.KeyDown             += CoreWindow_KeyDown;
            wnd.KeyUp               += CoreWindow_KeyUp;
            wnd.PointerPressed      += CoreWindow_PointerPressed;
            wnd.PointerWheelChanged += CoreWindow_PointerWheelChanged;
        }

        private void Win2DCanvas_Draw(Microsoft.Graphics.Canvas.UI.Xaml.CanvasControl sender, Microsoft.Graphics.Canvas.UI.Xaml.CanvasDrawEventArgs args) {
            if (double.IsNaN(LineHeight)) {
                // 最初の場合

                CanvasTextLayout layout = new CanvasTextLayout(args.DrawingSession, "M", TextFormat, float.MaxValue, float.MaxValue);
                LineHeight = layout.LayoutBounds.Height;
            }

            float view_w = (float)Win2DCanvas.ActualWidth;
            float view_h = (float)Win2DCanvas.ActualHeight;

            ViewLineCount = (int)(view_h / LineHeight);

            if (OverlappedButton.FocusState == FocusState.Unfocused) {

                args.DrawingSession.DrawRectangle(0, 0, view_w, view_h, Colors.Gray, 1);
            }
            else {

                args.DrawingSession.DrawRectangle(0, 0, view_w, view_h, Colors.Blue, 1);
            }

            int start_line_idx = (int)(EditScroll.VerticalOffset / LineHeight);
            int line_idx = 0;

            int pos;
            for(pos = 0; pos < Chars.Count && line_idx < start_line_idx; pos++) {
                if(Chars[pos].Chr == LF) {
                    line_idx++;
                    if(line_idx == start_line_idx) {
                        pos++;
                        break;
                    }
                }
            }


            float x_start = (float)ViewPadding.X, y = (float)ViewPadding.Y;

            int sel_start = Math.Min(SelOrigin, SelCurrent);
            int sel_end = Math.Max(SelOrigin, SelCurrent);          

            for (; ; pos++) {
                StringWriter line_sw = new StringWriter();

                float x = x_start;

                int start_pos = pos;
                for (; pos < Chars.Count;) {
                    StringWriter sw = new StringWriter();

                    UnderlineType under_line = Chars[pos].Underline;
                    bool selected = (sel_start <= pos && pos < sel_end);

                    int phrase_start_pos = pos;
                    for (; pos < Chars.Count && Chars[pos].Chr != LF && Chars[pos].Underline == under_line && (sel_start <= pos && pos < sel_end) == selected; pos++) {
                        sw.Write(Chars[pos].Chr);
                    }
                    //String str = new string((from c in Chars select c.Chr).ToArray());
                    String str = sw.ToString();

                    line_sw.Write(str);

                    Rect rc = (new CanvasTextLayout(args.DrawingSession, str, TextFormat, float.MaxValue, float.MaxValue)).LayoutBounds;

                    float xe = (float)(x + rc.Width);
                    float yb = (float)(y + rc.Height);
                    if (selected) {

                        args.DrawingSession.FillRectangle(x, y, (float)rc.Width, (float)rc.Height, Colors.Blue);
                        args.DrawingSession.DrawText(str, x, y, Colors.White, TextFormat);
                    }
                    else {
                        args.DrawingSession.DrawText(str, x, y, Colors.Black, TextFormat);

                        switch (under_line) {
                        case UnderlineType.None:
                        case UnderlineType.Undefined:
                            break;

                        case UnderlineType.Wave:
                            args.DrawingSession.DrawLine(x, yb, xe, yb, Colors.Blue, 1);
                            break;

                        case UnderlineType.Thick:
                            args.DrawingSession.DrawLine(x, yb, xe, yb, Colors.Black, 1);
                            break;

                        case UnderlineType.Thin:
                            args.DrawingSession.DrawLine(x, yb, xe, yb, Colors.Green, 1);
                            break;

                        default:
                            Debug.WriteLine("unknown under-line {0}", under_line);
                            break;
                        }
                    }

                    if(! double.IsNaN( ClickedPoint.X)) {

                        Rect phrase_rc = new Rect(x, y, rc.Width, rc.Height);

                        if (phrase_rc.Contains(ClickedPoint)) {

                            int phrase_pos;
                            StringWriter phrase_sw = new StringWriter();
                            for(phrase_pos = phrase_start_pos; phrase_pos <= pos; phrase_pos++) {

                                phrase_sw.Write(Chars[phrase_pos].Chr);
                                Rect rc2 = (new CanvasTextLayout(args.DrawingSession, phrase_sw.ToString(), TextFormat, float.MaxValue, float.MaxValue)).LayoutBounds;
                                Rect sub_phrase_rc = new Rect(x, y, rc2.Width, rc2.Height);
                                if (sub_phrase_rc.Contains(ClickedPoint)) {

                                    SetCursorPos(VirtualKey.None, phrase_pos);
                                    break;
                                }
                            }

                            ClickedPoint = new Point(double.NaN, double.NaN);
                        }
                    }

                    x += (float)rc.Width;

                    if (Chars.Count <= pos || Chars[pos].Chr == LF) {

                        break;
                    }
                }

                String line_str = line_sw.ToString();

                if (OverlappedButton.FocusState != FocusState.Unfocused && start_pos <= CursorPos && CursorPos <= pos) {

                    CanvasTextLayout textLayout = new CanvasTextLayout(args.DrawingSession, line_str.Substring(0, CursorPos - start_pos), TextFormat, float.MaxValue, float.MaxValue);
                    Rect rc = new Rect(x_start + textLayout.LayoutBounds.X, y + textLayout.LayoutBounds.Y, textLayout.LayoutBounds.Width, textLayout.LayoutBounds.Height);

                    float x1 = (float)rc.Right;
                    float y0 = (float)rc.Top;
                    float y1 = (float)rc.Bottom;

                    args.DrawingSession.DrawLine(x1, y0, x1, y1, Colors.Blue, 1);
                }

                line_idx++;
                if(ViewLineCount <= line_idx - start_line_idx) {
                    break;
                }

                CanvasTextLayout layout = new CanvasTextLayout(args.DrawingSession, line_str, TextFormat, float.MaxValue, float.MaxValue);
                y += (float)layout.LayoutBounds.Height;

                //Debug.WriteLine("Draw {0}-{1} {2}", sel_start, sel_end, line_str);

                if (Chars.Count <= pos) {

                    break;
                }
            }

            ClickedPoint = new Point(double.NaN, double.NaN);
        }

        private void Win2DCanvas_SizeChanged(object sender, SizeChangedEventArgs e) {
        }

        private void Win2DCanvas_PointerPressed(object sender, PointerRoutedEventArgs e) {
            Debug.WriteLine("Win2DCanvas_PointerPressed");

        }

        int GetLineTop(int current_pos) {
            int i;

            for (i = current_pos - 1; 0 <= i && Chars[i].Chr != LF; i--) ;
            return i + 1;
        }

        int GetNextLineTop(int current_pos) {
            for (int i = current_pos; i < Chars.Count; i++) {
                if (Chars[i].Chr == LF) {
                    return i + 1;
                }
            }

            return -1;
        }

        int GetLineEnd(int current_pos) {
            int i = GetNextLineTop(current_pos);

            return (i != -1 ? i - 1 : Chars.Count);
        }

        int GetLFCount(int start_pos, int end_pos) {
            int cnt = 0;

            for(int i = start_pos; i < end_pos; i++) {
                if(Chars[i].Chr == LF) {
                    cnt++;
                }
            }

            return cnt;
        }

        void ReplaceText(int sel_start, int sel_end, string new_text) {
            int old_LF_cnt = GetLFCount(sel_start, sel_end);

            Chars.RemoveRange(sel_start, sel_end - sel_start);

            TChar[] vc = new TChar[new_text.Length];
            for (int i = 0; i < new_text.Length; i++) {
                vc[i] = new TChar(new_text[i]);
            }

            Chars.InsertRange(sel_start, vc);


            SetCursorPos(VirtualKey.None, sel_start + new_text.Length);

            CoreTextRange modifiedRange;
            modifiedRange.StartCaretPosition = sel_start;
            modifiedRange.EndCaretPosition = sel_end;

            editContext.NotifyTextChanged(modifiedRange, new_text.Length, Selection);

            int new_LF_cnt = GetLFCount(sel_start, sel_start + new_text.Length);
            LineCount += new_LF_cnt - old_LF_cnt;

            if (! double.IsNaN(LineHeight)) {
                double document_height = LineCount * LineHeight;

                if(EditCanvas.Height != document_height) {

                    EditCanvas.Height = document_height;
                }
            }

            Win2DCanvas.Invalidate();
        }

        private async void CoreWindow_KeyDown(CoreWindow sender, KeyEventArgs e) {
            int current_line_top;
            int next_line_top;
            bool control_down = ((Window.Current.CoreWindow.GetKeyState(VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0);

            Debug.WriteLine("CoreWindow KeyDown : {0} {1} {2}", e.VirtualKey, OverlappedButton.FocusState, InComposition);

            if (editContext == null || InComposition || OverlappedButton.FocusState == FocusState.Unfocused) {
                return;
            }

            switch (e.VirtualKey) {
            case Windows.System.VirtualKey.Left:
                if (0 < CursorPos) {

                    SetCursorPos(e.VirtualKey, CursorPos - 1);
                    editContext.NotifySelectionChanged(Selection);
                }
                break;

            case Windows.System.VirtualKey.Right:
                if (CursorPos < Chars.Count) {

                    SetCursorPos(e.VirtualKey, CursorPos + 1);
                    editContext.NotifySelectionChanged(Selection);
                }

                break;

            case Windows.System.VirtualKey.Up: {
                    // 現在の行の先頭位置を得る。
                    current_line_top = GetLineTop(CursorPos);

                    if (current_line_top != 0) {
                        // 現在の行の先頭が文書の最初でない場合

                        // 直前の行の先頭位置を得る。
                        int prev_line_top = GetLineTop(current_line_top - 2);

                        // 直前の行の文字数。
                        int prev_line_len = (current_line_top - 1) - prev_line_top;

                        int col = Math.Min(prev_line_len, CursorPos - current_line_top);

                        SetCursorPos(e.VirtualKey, prev_line_top + col);
                    }
                }
                break;

            case Windows.System.VirtualKey.Down: {
                    // 現在の行の先頭位置を得る。
                    current_line_top = GetLineTop(CursorPos);

                    // 次の行の先頭位置を得る。
                    next_line_top = GetNextLineTop(CursorPos);

                    if (next_line_top != -1) {
                        // 次の行がある場合


                        // 次の次のの行の先頭位置を得る。
                        int next_next_line_top = GetNextLineTop(next_line_top);

                        // 次の行の文字数。
                        int next_line_len;
                        
                        if(next_next_line_top == -1) {

                            next_line_len = Chars.Count - next_line_top;
                        }
                        else {

                            next_line_len = next_next_line_top - 1 - next_line_top;
                        }

                        int col = Math.Min(next_line_len, CursorPos - current_line_top);

                        SetCursorPos(e.VirtualKey, next_line_top + col);
                    }
                }
                break;

            case Windows.System.VirtualKey.Home: {
                    int new_pos;

                    if ((Window.Current.CoreWindow.GetKeyState(VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0) {
                        // Controlキーが押されている場合

                        new_pos = 0;
                    }
                    else {
                        // Controlキーが押されてない場合

                        // 現在の行の先頭位置を得る。
                        new_pos = GetLineTop(CursorPos);
                    }

                    if (new_pos < CursorPos) {
                        SetCursorPos(e.VirtualKey, new_pos);
                        editContext.NotifySelectionChanged(Selection);
                    }
                }
                break;

            case Windows.System.VirtualKey.End: {
                    int new_pos;

                    if ((Window.Current.CoreWindow.GetKeyState(VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0) {
                        // Controlキーが押されている場合

                        new_pos = Chars.Count;
                    }
                    else {
                        // Controlキーが押されてない場合

                        // 現在の行の最終位置を得る。
                        new_pos = GetLineEnd(CursorPos);
                    }

                    if (CursorPos < new_pos) {

                        SetCursorPos(e.VirtualKey, new_pos);
                        editContext.NotifySelectionChanged(Selection);
                    }
                }
                break;

            case VirtualKey.PageUp: {
                    int line_diff = 0;
                    for(int i = CursorPos; 0 < i; i--) {
                        if(Chars[i].Chr == LF) {

                            line_diff++;
                            if (ViewLineCount <= line_diff) {

                                int line_idx = GetLFCount(0, i);
                                Debug.WriteLine("PageUp {0}", line_diff);
                                SetCursorPos(e.VirtualKey, i);
                                EditScroll.ScrollToVerticalOffset(Math.Min(EditCanvas.Height, line_idx * LineHeight));
                                editContext.NotifySelectionChanged(Selection);
                                break;
                            }
                        }
                    }
                }
                break;

            case VirtualKey.PageDown: {
                    int line_diff = 0;
                    for (int i = CursorPos; i < Chars.Count; i++) {
                        if (Chars[i].Chr == LF) {

                            line_diff++;
                            if (ViewLineCount <= line_diff) {

                                int line_idx = GetLFCount(0, i);
                                Debug.WriteLine("PageDown {0}", line_diff);
                                SetCursorPos(e.VirtualKey, i);
                                EditScroll.ScrollToVerticalOffset(Math.Min(EditCanvas.Height, line_idx * LineHeight));
                                editContext.NotifySelectionChanged(Selection);
                                break;
                            }
                        }
                    }
                }
                break;

            case Windows.System.VirtualKey.Back: {

                    if (0 < CursorPos) {

                        if (SelOrigin != -1) {

                            ReplaceText(SelStart, SelEnd, "");
                        }
                        else {

                            ReplaceText(CursorPos - 1, CursorPos, "");
                        }
                    }
                }
                break;

            case Windows.System.VirtualKey.Delete: {

                    if (CursorPos < Chars.Count) {

                        if(SelOrigin != -1) {

                            ReplaceText(SelStart, SelEnd, "");
                        }
                        else {

                            ReplaceText(CursorPos, CursorPos + 1, "");
                        }
                    }
                }
                break;

            case Windows.System.VirtualKey.Enter: {
                    ReplaceText(SelStart, SelEnd, "\n");
                }
                break;

            case VirtualKey.C:
                if (control_down && SelOrigin != -1) {

                    // https://msdn.microsoft.com/en-us/windows/uwp/app-to-app/copy-and-paste

                    int sel_start = Math.Min(SelOrigin, SelCurrent);
                    int sel_end = Math.Max(SelOrigin, SelCurrent);

                    char[] vc = new char[sel_end - sel_start];
                    for(int i = sel_start; i < sel_end; i++) {
                        vc[i] = Chars[i].Chr;
                    }

                    DataPackage dataPackage = new DataPackage();
                    dataPackage.RequestedOperation = DataPackageOperation.Copy;
                    dataPackage.SetText(new string(vc));
                    Clipboard.SetContent(dataPackage);
                }
                break;


            case VirtualKey.V:
                if (control_down) {

                    DataPackageView dataPackageView = Clipboard.GetContent();
                    if (dataPackageView.Contains(StandardDataFormats.Text)) {

                        int sel_start, sel_end;
                        if (SelOrigin != -1) {

                            sel_start = Math.Min(SelOrigin, SelCurrent);
                            sel_end = Math.Max(SelOrigin, SelCurrent);
                        }
                        else {
                            sel_start = CursorPos;
                            sel_end = CursorPos;
                        }

                        string text = await dataPackageView.GetTextAsync();

                        ReplaceText(sel_start, sel_end, text.Replace("\r\n", "\n"));
                    }
                }
                break;
            }

            if (SelOrigin != -1 && (Window.Current.CoreWindow.GetKeyState(VirtualKey.Shift) & CoreVirtualKeyStates.Down) == 0) {

                switch (e.VirtualKey) {
                case VirtualKey.Shift:
                case VirtualKey.Control:
                    break;

                default:
                    SelOrigin = -1;
                    SelCurrent = -1;
                    SelStart = CursorPos;
                    SelEnd = CursorPos;
                    Win2DCanvas.Invalidate();
                    break;
                }
            }
        }

        private void CoreWindow_KeyUp(CoreWindow sender, KeyEventArgs e) {
        }

        private void CoreWindow_PointerPressed(CoreWindow sender, PointerEventArgs e) {
            Point pt = Win2DCanvas.TransformToVisual(Window.Current.Content).TransformPoint(new Point(0, 0));

            ClickedPoint = new Point(e.CurrentPoint.Position.X - pt.X, e.CurrentPoint.Position.Y - pt.Y);

            Debug.WriteLine("CoreWindow PointerPressed {0} {1} {2}", e.CurrentPoint.Position, pt, ClickedPoint);
            Win2DCanvas.Invalidate();
        }


        private void CoreWindow_PointerWheelChanged(CoreWindow sender, PointerEventArgs args) {
            int scroll_direction = (0 < args.CurrentPoint.Properties.MouseWheelDelta ? -1 : 1);
            int offset = (int)Math.Round(EditScroll.VerticalOffset / LineHeight);
            EditScroll.ScrollToVerticalOffset((offset + scroll_direction) * LineHeight);
            //int line_idx = GetLFCount(0, CursorPos);
            Debug.WriteLine("Wheel {0}", args.CurrentPoint.Properties.MouseWheelDelta);
        }


        void SetCursorPos(VirtualKey key, int pos) {
            bool shift_down = ((Window.Current.CoreWindow.GetKeyState(VirtualKey.Shift) & CoreVirtualKeyStates.Down) != 0);

            if (shift_down && CursorPos != pos) {
                switch (key) {
                case Windows.System.VirtualKey.Left:
                case Windows.System.VirtualKey.Right:
                case Windows.System.VirtualKey.Up:
                case Windows.System.VirtualKey.Down:
                case Windows.System.VirtualKey.Home:
                case Windows.System.VirtualKey.End:

                    if (SelOrigin == -1) {

                        SelOrigin = CursorPos;
                    }
                    SelCurrent = pos;

                    break;
                }
            }

            Debug.WriteLine("Set Cursor Pos {0}", pos);
            CursorPos = pos;

            if (SelOrigin != -1) {

                SelStart = Math.Min(SelOrigin, SelCurrent);
                SelEnd = Math.Max(SelOrigin, SelCurrent);
            }
            else {

                SelStart = CursorPos;
                SelEnd = CursorPos;
            }

            Selection.StartCaretPosition = pos;
            Selection.EndCaretPosition = pos;

            Win2DCanvas.Invalidate();
        }

        private void EditScroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e) {
            Win2DCanvas.Invalidate();
        }

        //private void OverlappedButton_KeyDown(object sender, KeyRoutedEventArgs e) {
        //    Debug.WriteLine("ボタン KeyDown : {0} {1} {2}", e.Key, OverlappedButton.FocusState, InComposition);
        //}


        string CurrentLineString() {
            return new string((from c in Chars select c.Chr).ToArray());
        }
    }

    public class TChar {
        public char Chr;
        public UnderlineType Underline;

        public TChar(char c) {
            Chr = c;
        }
    }

    public class TLine {
        public List<TChar> Chars = new List<TChar>();
    }
}
