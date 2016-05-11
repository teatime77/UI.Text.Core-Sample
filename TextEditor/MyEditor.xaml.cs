﻿using System;
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
        // 改行文字
        const char LF = '\n';

        // 文書内のテキスト
        List<TChar> Chars = new List<TChar>();

        // 描画した図形のリスト
        List<TShape> DrawList = new List<TShape>();

        // フォントなどの書式
        CanvasTextFormat TextFormat = new CanvasTextFormat();

        // かな漢字変換の途中ならtrue
        bool InComposition = false;

        // テキストの選択を始めた位置
        int SelOrigin = 0;

        // 現在のテキストの選択位置(カーソル位置)
        int SelCurrent = 0;

        // 選択したテキストをマウスで別の場所にドロップする位置
        int DropPos = -1;

        // 文書の行数
        int LineCount = 1;

        // 1行の高さ
        double LineHeight = double.NaN;

        // 空白の1文字の幅
        double SpaceWidth;

        // ビュー内の行数
        int ViewLineCount;

        // ビュー内の描画開始位置
        Point ViewPadding = new Point(5, 5);

        // マウスのイベントハンドラのコルーチン
        IEnumerator PointerLoop;

        // マウスのイベントハンドラで使うタイマー 
        DispatcherTimer PointerTimer;

        // マウスのイベントハンドラで使うイベントのタイプ
        EEvent PointerEventType = EEvent.Undefined;

        // 現在のイベントオブジェクト
        PointerEventArgs CurrentPointerEvent;

        // 矢印カーソル
        CoreCursor ArrowCoreCursor = new CoreCursor(CoreCursorType.Arrow, 1);

        // Iカーソル
        CoreCursor IBeamCoreCursor = new CoreCursor(CoreCursorType.IBeam, 2);

        /*
            テキスト選択の開始位置
        */
        public int SelStart{
            get {
                return Math.Min(SelOrigin, SelCurrent);
            }
        }

        /*
            テキスト選択の終了位置
        */
        public int SelEnd {
            get {
                return Math.Max(SelOrigin, SelCurrent);
            }
        }

        /*
            コンストラクタ
        */
        public MyEditor() {
            this.InitializeComponent();
            Debug.WriteLine("<<--- Initialize");

            //TextFormat.FontSize = 48;
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

        /*
            指定した文字列のサイズを計算します。
        */
        Size MeasureText(string str, CanvasTextFormat text_format) {
            // 文字列が空白だけの場合、CanvasTextLayoutの計算が正しくない。
            // https://github.com/Microsoft/Win2D/issues/103

            // 空白を除いた文字列のサイズを計算します。
            string str_no_space = str.Replace(" ", "");
            Rect rc = (new CanvasTextLayout(Win2DCanvas, str_no_space, text_format, float.MaxValue, float.MaxValue)).LayoutBounds;

            // 空白を除いた文字列の幅に空白の幅を加えます。
            return new Size(rc.Width + (str.Length - str_no_space.Length) * SpaceWidth, rc.Height);
        }

        private void Win2DCanvas_Draw(Microsoft.Graphics.Canvas.UI.Xaml.CanvasControl sender, Microsoft.Graphics.Canvas.UI.Xaml.CanvasDrawEventArgs args) {
            Debug.WriteLine("<<--- Draw");

            DrawList.Clear();
            if (double.IsNaN(LineHeight)) {
                // 最初の場合


                Rect M_rc = new CanvasTextLayout(args.DrawingSession, "M", TextFormat, float.MaxValue, float.MaxValue).LayoutBounds;
                LineHeight = M_rc.Height;

                Rect sp_M_rc = new CanvasTextLayout(args.DrawingSession, " M", TextFormat, float.MaxValue, float.MaxValue).LayoutBounds;
                SpaceWidth = sp_M_rc.Width - M_rc.Width;
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

            int sel_start = SelStart;
            int sel_end   = SelEnd;

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

                    DrawList.Add(new TShape(x, y, sz, phrase_start_pos, pos + 1));

                    x += (float)sz.Width;

                    if (Chars.Count <= pos || Chars[pos].Chr == LF) {

                        break;
                    }
                }

                String line_str = line_sw.ToString();

                // 挿入カーソルの位置を得ます。
                int cursor_pos;
                if(DropPos != -1) {
                    // ドロップ先がある場合

                    // ドロップ先に挿入カーソルを描画します。
                    cursor_pos = DropPos;
                }
                else {
                    // ドロップ先がない場合

                    // 現在の選択位置に挿入カーソルを描画します。
                    cursor_pos = SelCurrent;
                }

                if (OverlappedButton.FocusState != FocusState.Unfocused && start_pos <= cursor_pos && cursor_pos <= pos) {

                    Size current_sz = MeasureText(line_str.Substring(0, cursor_pos - start_pos), TextFormat);

                    float x1 = (float)(x_start + current_sz.Width);
                    float y0 = y;
                    float y1 = (float)(y + current_sz.Height);

                    args.DrawingSession.DrawLine(x1, y0, x1, y1, Colors.Blue, 1);
                }

                line_idx++;
                if(ViewLineCount <= line_idx - start_line_idx) {
                    break;
                }

                // 現在の行の高さを計算して、yに加算します。
                y += (float)MeasureText(line_str, TextFormat).Height;

                if (Chars.Count <= pos) {
                    // 文書の最後の場合

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

            SelOrigin   = sel_start + new_text.Length;
            SelCurrent  = SelOrigin;

            CoreTextRange modifiedRange;
            modifiedRange.StartCaretPosition = sel_start;
            modifiedRange.EndCaretPosition = sel_end;

            CoreTextRange new_range;
            new_range.StartCaretPosition = SelStart;
            new_range.EndCaretPosition = SelEnd;
            editContext.NotifyTextChanged(modifiedRange, new_text.Length, new_range);

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

        void ChangeSelection(KeyEventArgs e) {
            int old_sel_current = SelCurrent;
            int new_sel_current = SelCurrent;
            int current_line_top;
            int next_line_top;

            switch (e.VirtualKey) {
            case VirtualKey.Left: // 左矢印(←)
                if (0 < SelCurrent) {

                    new_sel_current = SelCurrent - 1;
                }
                break;

            case VirtualKey.Right: // 右矢印(→)
                if (SelCurrent < Chars.Count) {

                    new_sel_current = SelCurrent + 1;
                }
                break;

            case VirtualKey.Up: { // 上矢印(↑)
                    // 現在の行の先頭位置を得る。
                    current_line_top = GetLineTop(SelCurrent);

                    if (current_line_top != 0) {
                        // 現在の行の先頭が文書の最初でない場合

                        // 直前の行の先頭位置を得る。
                        int prev_line_top = GetLineTop(current_line_top - 2);

                        // 直前の行の文字数。
                        int prev_line_len = (current_line_top - 1) - prev_line_top;

                        int col = Math.Min(prev_line_len, SelCurrent - current_line_top);

                        new_sel_current = prev_line_top + col;
                    }
                }
                break;

            case VirtualKey.Down: { // 下矢印(↓)
                    // 現在の行の先頭位置を得る。
                    current_line_top = GetLineTop(SelCurrent);

                    // 次の行の先頭位置を得る。
                    next_line_top = GetNextLineTop(SelCurrent);

                    if (next_line_top != -1) {
                        // 次の行がある場合


                        // 次の次のの行の先頭位置を得る。
                        int next_next_line_top = GetNextLineTop(next_line_top);

                        // 次の行の文字数。
                        int next_line_len;

                        if (next_next_line_top == -1) {

                            next_line_len = Chars.Count - next_line_top;
                        }
                        else {

                            next_line_len = next_next_line_top - 1 - next_line_top;
                        }

                        int col = Math.Min(next_line_len, SelCurrent - current_line_top);

                        new_sel_current = next_line_top + col;
                    }
                }
                break;

            case VirtualKey.Home:
                if ((Window.Current.CoreWindow.GetKeyState(VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0) {
                    // Controlキーが押されている場合

                    new_sel_current = 0;
                }
                else {
                    // Controlキーが押されてない場合

                    // 現在の行の先頭位置を得る。
                    new_sel_current = GetLineTop(SelCurrent);
                }
                break;

            case VirtualKey.End: 
                if ((Window.Current.CoreWindow.GetKeyState(VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0) {
                    // Controlキーが押されている場合

                    new_sel_current = Chars.Count;
                }
                else {
                    // Controlキーが押されてない場合

                    // 現在の行の最終位置を得る。
                    new_sel_current = GetLineEnd(SelCurrent);
                }               
                break;

            case VirtualKey.PageUp: {
                    int line_diff = 0;
                    for (int i = SelCurrent; 0 < i; i--) {
                        if (Chars[i].Chr == LF) {

                            line_diff++;
                            if (ViewLineCount <= line_diff) {

                                int line_idx = GetLFCount(0, i);
                                Debug.WriteLine("PageUp {0}", line_diff);
                                new_sel_current = i;
                                EditScroll.ScrollToVerticalOffset(Math.Min(EditCanvas.Height, line_idx * LineHeight));
                                break;
                            }
                        }
                    }
                }
                break;

            case VirtualKey.PageDown: {
                    int line_diff = 0;
                    for (int i = SelCurrent; i < Chars.Count; i++) {
                        if (Chars[i].Chr == LF) {

                            line_diff++;
                            if (ViewLineCount <= line_diff) {

                                int line_idx = GetLFCount(0, i);
                                Debug.WriteLine("PageDown {0}", line_diff);
                                new_sel_current = i;
                                EditScroll.ScrollToVerticalOffset(Math.Min(EditCanvas.Height, line_idx * LineHeight));
                                break;
                            }
                        }
                    }
                }
                break;
            }

            SetSelection(new_sel_current);
        }

        private async void CoreWindow_KeyDown(CoreWindow sender, KeyEventArgs e) {
            bool control_down = ((Window.Current.CoreWindow.GetKeyState(VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0);

            Debug.WriteLine("<<--- CoreWindow KeyDown : {0} {1} {2}", e.VirtualKey, OverlappedButton.FocusState, InComposition);

            if (editContext == null || InComposition || OverlappedButton.FocusState == FocusState.Unfocused) {
                return;
            }

            switch (e.VirtualKey) {
            case VirtualKey.Left:   // 左矢印(←)
            case VirtualKey.Right:  // 右矢印(→)
            case VirtualKey.Up:     // 上矢印(↑)
            case VirtualKey.Down:  // 下矢印(↓)
            case VirtualKey.Home:
            case VirtualKey.End:
            case VirtualKey.PageUp:
            case VirtualKey.PageDown:
                ChangeSelection(e);
                break;
            }

            switch (e.VirtualKey) {
            case VirtualKey.Back: {

                    if (SelOrigin != SelCurrent) {

                        ReplaceText(SelStart, SelEnd, "");
                    }
                    else if (0 < SelCurrent) {

                        ReplaceText(SelCurrent - 1, SelCurrent, "");
                    }
                }
                break;

            case VirtualKey.Delete: {

                    if (SelOrigin != SelCurrent) {

                        ReplaceText(SelStart, SelEnd, "");
                    }
                    else if (SelCurrent < Chars.Count) {

                        ReplaceText(SelCurrent, SelCurrent + 1, "");
                    }
                }
                break;

            case VirtualKey.Enter: {
                    ReplaceText(SelStart, SelEnd, "\n");
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

                        string text = await dataPackageView.GetTextAsync();

                        ReplaceText(SelStart, SelEnd, text.Replace("\r\n", "\n"));
                    }
                }
                break;
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
                    double prev_width = 0;
                    for (phrase_pos = shape.StartPos; phrase_pos <= shape.EndPos; phrase_pos++) {

                        phrase_sw.Write(Chars[phrase_pos].Chr);

                        Size sz = MeasureText(phrase_sw.ToString(), TextFormat);

                        // 現在の文字の幅
                        double this_char_width = sz.Width - prev_width;

                        // 現在の文字の左端より文字幅の20%ぐらい左を、矩形の左端にします。
                        Rect sub_phrase_rc = new Rect(shape.Bounds.X - this_char_width * 0.2, shape.Bounds.Y, sz.Width, sz.Height);
                        if (sub_phrase_rc.Contains(pt)) {
                            // 矩形に含まれる場合

                            return phrase_pos;
                        }

                        prev_width = sz.Width;
                    }
                }
            }

            return -1;
        }

        void HandlePointerEvent(EEvent event_type, PointerEventArgs e) {
            if (PointerLoop != null) {
                PointerEventType = event_type;
                CurrentPointerEvent = e;
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
            HandlePointerEvent(EEvent.PointerMoved, e);
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

            int start_pos = TextPositionFromPointer(CurrentPointerEvent.CurrentPoint);

            PointerTimer.Interval = TimeSpan.FromMilliseconds(500);
            PointerTimer.Start();
            yield return 0;

            while (event_type == EEvent.Undefined) {

                switch (PointerEventType) {
                case EEvent.Timeout:
                    // 長押しの場合

                    event_type = EEvent.LongPress;
                    break;
                case EEvent.PointerMoved:
                    // ドラッグの場合

                    event_type = EEvent.Drag;
                    break;

                case EEvent.PointerReleased:
                    // クリックの場合

                    PointerTimer.Interval = TimeSpan.FromMilliseconds(200);
                    PointerTimer.Start();
                    yield return 0;

                    while (event_type == EEvent.Undefined) {
                        switch (PointerEventType) {
                        case EEvent.Timeout:
                            // ダブルクリックでない場合

                            Debug.WriteLine("クリック");
                            SetSelection(start_pos);
                            PointerLoop = null;
                            yield break;

                        case EEvent.PointerPressed:
                            // ダブルクリックの場合

                            Debug.WriteLine("ダブルクリック");
                            if (start_pos != -1) {
                                // マウス位置にテキストがある場合

                                // 語句の始まりと終わりを探します。
                                // 語句の文字はIsLetterOrDigitか'_'とします。

                                // 語句の始まりを探します。
                                int phrase_start = start_pos;
                                for (; 0 <= phrase_start && (Char.IsLetterOrDigit(Chars[phrase_start].Chr) || Chars[phrase_start].Chr == '_'); phrase_start--) ;
                                phrase_start++;

                                // 語句の終わりを探します。
                                int phrase_end = start_pos;
                                for (; phrase_end < Chars.Count && (Char.IsLetterOrDigit(Chars[phrase_end].Chr) || Chars[phrase_end].Chr == '_'); phrase_end++) ;

                                // 語句の始まりと終わりを選択します
                                SelOrigin = phrase_start;
                                SelCurrent = phrase_end;

                                MyNotifySelectionChanged();
                                Win2DCanvas.Invalidate();
                            }

                            PointerLoop = null;
                            yield break;

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

            if(event_type == EEvent.LongPress) {
                // 長押しの場合

                if (SelCurrent != SelOrigin) {
                    // テキストを選択している場合

                    // マウスの真下のテキストの位置を得ます。
                    int pos = TextPositionFromPointer(e.CurrentPoint);

                    if (SelStart <= pos && pos < SelEnd) {
                        // 選択されているテキストを長押しした場合

                        // マウスカーソルを矢印に変えます。
                        CoreApplication.GetCurrentView().CoreWindow.PointerCursor = ArrowCoreCursor;

                        while (true) {
                            switch (PointerEventType) {
                            case EEvent.PointerMoved:
                                // ドラッグの場合

                                int drag_pos = TextPositionFromPointer(CurrentPointerEvent.CurrentPoint);
                                if (drag_pos != -1 && !(SelStart <= drag_pos && drag_pos < SelEnd)) {

                                    DropPos = drag_pos;
                                    Debug.WriteLine("ドロップ中 {0}", DropPos);
                                }
                                else {

                                    DropPos = -1;
                                }

                                Win2DCanvas.Invalidate();
                                yield return 0;

                                break;

                            case EEvent.PointerReleased:
                                // リリースの場合

                                if (DropPos != -1) {

                                    string sel_str = new string((from c in Chars.Skip(SelStart) select c.Chr).Take(SelEnd - SelStart).ToArray());

                                    if ((Window.Current.CoreWindow.GetKeyState(VirtualKey.Control) & CoreVirtualKeyStates.Down) == 0) {
                                        // Ctrlキーが押されてない場合

                                        bool drop_after_selection = (SelStart < DropPos);

                                        // 選択されたテキストを削除します。
                                        ReplaceText(SelStart, SelEnd, "");

                                        if (drop_after_selection) {
                                            // ドロップ位置が選択位置より後ろの場合

                                            // ドロップ位置を選択テキストの長さだけ引きます。
                                            DropPos -= sel_str.Length;
                                        }
                                    }

                                    // ドロップ位置に選択テキストを挿入します。
                                    ReplaceText(DropPos, DropPos, sel_str);

                                    DropPos = -1;
                                }

                                // マウスカーソルをIカーソルに戻します。
                                CoreApplication.GetCurrentView().CoreWindow.PointerCursor = IBeamCoreCursor;

                                Win2DCanvas.Invalidate();
                                PointerLoop = null;
                                yield break;

                            default:
                                yield return 0;
                                break;
                            }
                        }
                    }
                }
            }

            if (event_type == EEvent.LongPress || event_type == EEvent.Drag) {
                // 長押しかドラッグの場合

                if (start_pos == -1) {

                    PointerLoop = null;
                    yield break;
                }

                SelOrigin = start_pos;
                SelCurrent = start_pos;
                Debug.WriteLine("ドラッグの選択の始め {0}", start_pos);

                Win2DCanvas.Invalidate();
                yield return 0;

                while (true) {
                    switch (PointerEventType) {
                    case EEvent.PointerMoved:
                        // ドラッグの場合

                        int pos = TextPositionFromPointer(CurrentPointerEvent.CurrentPoint);
                        if (pos != -1) {

                            SelCurrent = pos;
                            Debug.WriteLine("ドラッグして選択 {0}", pos);

                            Win2DCanvas.Invalidate();
                        }

                        yield return 0;
                        break;

                    case EEvent.PointerReleased:
                        // リリースの場合

                        MyNotifySelectionChanged();
                        PointerLoop = null;
                        yield break;

                    default:
                        yield return 0;
                        break;
                    }
                }
            }

            PointerLoop = null;
        }

        private void CoreWindow_PointerWheelChanged(CoreWindow sender, PointerEventArgs args) {
            int scroll_direction = (0 < args.CurrentPoint.Properties.MouseWheelDelta ? -1 : 1);
            int offset = (int)Math.Round(EditScroll.VerticalOffset / LineHeight);
            EditScroll.ScrollToVerticalOffset((offset + scroll_direction) * LineHeight);
            Debug.WriteLine("<<--- PointerWheelChanged {0}", args.CurrentPoint.Properties.MouseWheelDelta);
        }

        void SetSelection(int pos) {
            if(pos != -1 && pos != SelCurrent) {
                // 選択位置が変わった場合

                SelCurrent = pos;

                if ((Window.Current.CoreWindow.GetKeyState(VirtualKey.Shift) & CoreVirtualKeyStates.Down) == 0) {
                    // シフトキーが押されてない場合

                    SelOrigin = SelCurrent;
                }

                MyNotifySelectionChanged();
                Win2DCanvas.Invalidate();
            }
        }

        private void EditScroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e) {
            Debug.WriteLine("<<--- ViewChanged");
            Win2DCanvas.Invalidate();
        }

        string StringFromRange(int start_pos, int end_pos) {
            return new string((from c in Chars.GetRange(start_pos, end_pos - start_pos) select c.Chr).ToArray());
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
        PointerMoved,
        PointerReleased,

        LongPress,
        Drag,

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

    /*
        書式付きの文字のクラス
        本当はstructの方がよいですが、structだと以下のようなことができないのでclassにしています。
            Chars[i].Underline = 代入値;
    */
    public class TChar {
        // 文字
        public char Chr;

        // 下線
        public UnderlineType Underline;

        /*
            コンストラクタ
        */
        public TChar(char c) {
            Chr = c;
        }
    }

    /*
        描画される図形のクラス
        現在は文字列描画のみですが、将来的には画像を描画することも考えています。
    */
    public class TShape {
        // 図形を囲む矩形
        public Rect     Bounds;

        // 図形に対応するテキストの範囲
        public int StartPos;
        public int EndPos;

        /*
            コンストラクタ
        */
        public TShape(double x, double y, Size sz, int start_pos, int end_pos) {
            Bounds.X        = x;
            Bounds.Y        = y;
            Bounds.Width    = sz.Width;
            Bounds.Height   = sz.Height;
            StartPos        = start_pos;
            EndPos          = end_pos;
        }
    }
}
