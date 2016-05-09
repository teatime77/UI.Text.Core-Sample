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
using Windows.UI.Input;
using System.Collections;


// The User Control item template is documented at http://go.microsoft.com/fwlink/?LinkId=234236

namespace MyEdit {
    public sealed partial class MyEditor : UserControl {
        const char LF = '\n';
        int CursorPos = 0;
        List<TChar> Chars = new List<TChar>();
        List<TShape> DrawList = new List<TShape>();
        CanvasTextFormat TextFormat = new CanvasTextFormat();
        bool InComposition = false;
        int SelOrigin = -1;
        int SelCurrent = -1;
        int LineCount = 1;
        double LineHeight = double.NaN;
        int ViewLineCount;
        Point ViewPadding = new Point(50, 50);
        IEnumerator PointerLoop;
        DispatcherTimer PointerTimer;
        EEvent PointerEventType = EEvent.Undefined;

        CoreCursor ArrowCoreCursor = new CoreCursor(CoreCursorType.Arrow, 1);
        CoreCursor IBeamCoreCursor = new CoreCursor(CoreCursorType.IBeam, 2);

        // テキストの選択位置
        CoreTextRange Selection;

        /*
            コンストラクタ
        */
        public MyEditor() {
            this.InitializeComponent();
            Debug.WriteLine("<<--- Initialize");

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
            wnd.PointerMoved        += CoreWindow_PointerMoved;
            wnd.PointerReleased     += CoreWindow_PointerReleased;
            wnd.PointerWheelChanged += CoreWindow_PointerWheelChanged;

            PointerTimer = new DispatcherTimer();
            PointerTimer.Tick += PointerTimer_Tick;
        }

        Size MeasureText(string str, CanvasTextFormat text_format) {
            Rect rc = (new CanvasTextLayout(Win2DCanvas, str, text_format, float.MaxValue, float.MaxValue)).LayoutBounds;

            return new Size(rc.Width, rc.Height);
        }

        private void Win2DCanvas_Draw(Microsoft.Graphics.Canvas.UI.Xaml.CanvasControl sender, Microsoft.Graphics.Canvas.UI.Xaml.CanvasDrawEventArgs args) {
            Debug.WriteLine("<<--- Draw");

            DrawList.Clear();
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

            float x_start = (float)ViewPadding.X;
            float y = (float)ViewPadding.Y;

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

                    Size sz = MeasureText(str, TextFormat);

                    float xe = (float)(x + sz.Width);
                    float yb = (float)(y + sz.Height);
                    if (selected) {

                        args.DrawingSession.FillRectangle(x, y, (float)sz.Width, (float)sz.Height, Colors.Blue);
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

                    DrawList.Add(new TShape(x, y, sz, phrase_start_pos, pos));

                    x += (float)sz.Width;

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

            Debug.WriteLine("<<--- CoreWindow KeyDown : {0} {1} {2}", e.VirtualKey, OverlappedButton.FocusState, InComposition);

            if (editContext == null || InComposition || OverlappedButton.FocusState == FocusState.Unfocused) {
                return;
            }

            switch (e.VirtualKey) {
            case VirtualKey.Left: // 左矢印(←)
                if (0 < CursorPos) {

                    SetCursorPos(e.VirtualKey, CursorPos - 1);
                    editContext.NotifySelectionChanged(Selection);
                }
                break;

            case VirtualKey.Right: // 右矢印(→)
                if (CursorPos < Chars.Count) {

                    SetCursorPos(e.VirtualKey, CursorPos + 1);
                    editContext.NotifySelectionChanged(Selection);
                }

                break;

            case VirtualKey.Up: { // 上矢印(↑)
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

            case VirtualKey.Down: { // 下矢印(↓)
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

            case VirtualKey.Home: {
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

            case VirtualKey.End: {
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

            case VirtualKey.Back: {

                    if (0 < CursorPos) {

                        if (SelOrigin != -1) {

                            ReplaceText(Selection.StartCaretPosition, Selection.EndCaretPosition, "");
                        }
                        else {

                            ReplaceText(CursorPos - 1, CursorPos, "");
                        }
                    }
                }
                break;

            case VirtualKey.Delete: {

                    if (CursorPos < Chars.Count) {

                        if(SelOrigin != -1) {

                            ReplaceText(Selection.StartCaretPosition, Selection.EndCaretPosition, "");
                        }
                        else {

                            ReplaceText(CursorPos, CursorPos + 1, "");
                        }
                    }
                }
                break;

            case VirtualKey.Enter: {
                    ReplaceText(Selection.StartCaretPosition, Selection.EndCaretPosition, "\n");
                }
                break;

            case VirtualKey.C:
                if (control_down && SelOrigin != -1) {
                    // Ctrl+Cで選択中の場合

                    // https://msdn.microsoft.com/en-us/windows/uwp/app-to-app/copy-and-paste

                    int sel_start = Math.Min(SelOrigin, SelCurrent);
                    int sel_end = Math.Max(SelOrigin, SelCurrent);

                    char[] vc = new char[sel_end - sel_start];
                    for(int i = 0; i < vc.Length; i++) {
                        vc[i] = Chars[sel_start + i].Chr;
                    }

                    DataPackage dataPackage = new DataPackage();
                    dataPackage.RequestedOperation = DataPackageOperation.Copy;
                    dataPackage.SetText(new string(vc).Replace("\n", "\r\n"));
                    Clipboard.SetContent(dataPackage);
                }
                break;


            case VirtualKey.V:
                if (control_down) {
                    // Ctrl+Vの場合

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
                // 選択中でシフトキーが押されてない場合

                switch (e.VirtualKey) {
                case VirtualKey.Shift:
                case VirtualKey.Control:
                    break;

                default:
                    // 選択状態を解除する。

                    SelOrigin = -1;
                    SelCurrent = -1;
                    Selection.StartCaretPosition = CursorPos;
                    Selection.EndCaretPosition = CursorPos;

                    Win2DCanvas.Invalidate();
                    break;
                }
            }
        }

        private void CoreWindow_KeyUp(CoreWindow sender, KeyEventArgs e) {
            Debug.WriteLine("<<--- KeyUp");
        }

        int TextPositionFromPointer(PointerPoint pointer) {
            Point canvas_pos = Win2DCanvas.TransformToVisual(Window.Current.Content).TransformPoint(new Point(0, 0));

            Point pt = new Point(pointer.Position.X - canvas_pos.X, pointer.Position.Y - canvas_pos.Y);

            foreach (TShape shape in DrawList) {
                if (shape.Bounds.Contains(pt)) {

                    int phrase_pos;
                    StringWriter phrase_sw = new StringWriter();
                    for (phrase_pos = shape.Range.StartPos; phrase_pos <= shape.Range.EndPos; phrase_pos++) {

                        phrase_sw.Write(Chars[phrase_pos].Chr);

                        Size sz = MeasureText(phrase_sw.ToString(), TextFormat);
                        Rect sub_phrase_rc = new Rect(shape.Bounds.X, shape.Bounds.Y, sz.Width, sz.Height);
                        if (sub_phrase_rc.Contains(pt)) {

                            return phrase_pos;
                        }
                    }
                }
            }

            return -1;
        }

        void HandlePointerEvent(EEvent event_type, Object event_args) {
            if (PointerLoop != null) {
                PointerEventType = event_type;
                PointerLoop.MoveNext();
            }
        }

        private void CoreWindow_PointerPressed(CoreWindow sender, PointerEventArgs e) {
            if(editContext == null) {
                return;
            }

            if(PointerLoop == null) {

                PointerLoop = PointerHandler(e);
            }

            HandlePointerEvent(EEvent.PointerPressed, e);
        }

        private void CoreWindow_PointerMoved(CoreWindow sender, PointerEventArgs e) {
            if (e.CurrentPoint.Properties.IsLeftButtonPressed && PointerLoop != null) {

                int pos = TextPositionFromPointer(e.CurrentPoint);
                if (pos != -1) {

                    CursorPos = pos;
                    SelCurrent = CursorPos;

                    if (SelOrigin != -1) {

                        Selection.StartCaretPosition = Math.Min(SelOrigin, SelCurrent);
                        Selection.EndCaretPosition = Math.Max(SelOrigin, SelCurrent);
                    }
                    else {

                        Selection.StartCaretPosition = CursorPos;
                        Selection.EndCaretPosition = CursorPos;
                    }

                    Win2DCanvas.Invalidate();
                }
            }

        }

        private void CoreWindow_PointerReleased(CoreWindow sender, PointerEventArgs e) {
            HandlePointerEvent(EEvent.PointerReleased, e);
            Debug.WriteLine("<<--- CoreWindow PointerReleased");
        }

        private void PointerTimer_Tick(object sender, object e) {
            Debug.WriteLine("<<--- PointerTimer");
            PointerTimer.Stop();

            HandlePointerEvent(EEvent.Timeout, null);
        }

        public IEnumerator PointerHandler(PointerEventArgs e) {
            EEvent event_type = EEvent.Undefined;

            Debug.Assert(PointerEventType == EEvent.PointerPressed);

            PointerTimer.Interval = TimeSpan.FromMilliseconds(500);
            PointerTimer.Start();
            yield return 0;

            while (event_type == EEvent.Undefined) {

                switch (PointerEventType) {
                case EEvent.Timeout:
                    // 長押しの場合

                    event_type = EEvent.LongPress;
                    break;

                case EEvent.PointerReleased:
                    // クリックの場合

                    PointerTimer.Interval = TimeSpan.FromMilliseconds(500);
                    PointerTimer.Start();
                    yield return 0;

                    while (event_type == EEvent.Undefined) {
                        switch (PointerEventType) {
                        case EEvent.Timeout:
                            // ダブルクリックでない場合

                            event_type = EEvent.Tapped;
                            break;

                        case EEvent.PointerPressed:
                            // ダブルクリックの場合

                            event_type = EEvent.DoubleTapped;
                            break;

                        default:
                            yield return 0;
                            break;
                        }
                    }
                    break;

                default:
                    yield return 0;
                    break;
                }
            }

            switch (event_type) {
            case EEvent.Tapped:

                int pos = TextPositionFromPointer(e.CurrentPoint);
                if (pos != -1) {

                    SetCursorPos(VirtualKey.None, pos);
                    SelOrigin = CursorPos;
                    SelCurrent = CursorPos;
                }

                Debug.WriteLine("<<--- PointerHandler {0} {1}", event_type, pos);
                Win2DCanvas.Invalidate();
                break;

            default:
                Debug.WriteLine("<<--- PointerHandler {0}", event_type);
                break;
            }

            PointerLoop = null;
        }

        private void CoreWindow_PointerWheelChanged(CoreWindow sender, PointerEventArgs args) {
            int scroll_direction = (0 < args.CurrentPoint.Properties.MouseWheelDelta ? -1 : 1);
            int offset = (int)Math.Round(EditScroll.VerticalOffset / LineHeight);
            EditScroll.ScrollToVerticalOffset((offset + scroll_direction) * LineHeight);
            Debug.WriteLine("<<--- PointerWheelChanged {0}", args.CurrentPoint.Properties.MouseWheelDelta);
        }

        void SetCursorPos(VirtualKey key, int pos) {
            bool shift_down = ((Window.Current.CoreWindow.GetKeyState(VirtualKey.Shift) & CoreVirtualKeyStates.Down) != 0);

            if (shift_down && CursorPos != pos) {
                switch (key) {
                case VirtualKey.Left:
                case VirtualKey.Right:
                case VirtualKey.Up:
                case VirtualKey.Down:
                case VirtualKey.Home:
                case VirtualKey.End:

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

                Selection.StartCaretPosition = Math.Min(SelOrigin, SelCurrent);
                Selection.EndCaretPosition = Math.Max(SelOrigin, SelCurrent);
            }
            else {

                Selection.StartCaretPosition = CursorPos;
                Selection.EndCaretPosition = CursorPos;
            }

            Win2DCanvas.Invalidate();
        }

        private void EditScroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e) {
            Debug.WriteLine("<<--- ViewChanged");
            Win2DCanvas.Invalidate();
        }

        string CurrentLineString() {
            return new string((from c in Chars select c.Chr).ToArray());
        }

        private void OverlappedButton_PointerEntered(object sender, PointerRoutedEventArgs e) {
            CoreApplication.GetCurrentView().CoreWindow.PointerCursor = IBeamCoreCursor;

        }

        private void OverlappedButton_PointerExited(object sender, PointerRoutedEventArgs e) {
            CoreApplication.GetCurrentView().CoreWindow.PointerCursor   = ArrowCoreCursor;

        }
    }

    public enum EEvent {
        Undefined,
        Timeout,

        Initialize,
        Loaded,
        GotFocus,
        LostFocus,
        Draw,

        KeyDown,
        KeyUp,
        PointerPressed,
        PointerReleased,

        Tapped,
        DoubleTapped,
        LongPress,

        PointerWheelChanged,
        ViewChanged,

        InputLanguageChanged,
        TextUpdating,
        SelectionUpdating,
        FormatUpdating,

        TextRequested,
        SelectionRequested,
        LayoutRequested,

        NotifyFocusLeaveCompleted,
        CompositionStarted,
        CompositionCompleted,
        FocusRemoved,
    }

    public class TChar {
        public char Chr;
        public UnderlineType Underline;

        public TChar(char c) {
            Chr = c;
        }
    }

    public struct TRange {
        public Int32 StartPos;
        public Int32 EndPos;
    }

    public class TShape {
        public Rect     Bounds;
        public TRange   Range;

        public TShape(double x, double y, Size sz, int start_pos, int end_pos) {
            Bounds.X        = x;
            Bounds.Y        = y;
            Bounds.Width    = sz.Width;
            Bounds.Height   = sz.Height;
            Range.StartPos  = start_pos;
            Range.EndPos    = end_pos;
        }
    }

    public class TLine {
        public List<TChar> Chars = new List<TChar>();
    }
}
