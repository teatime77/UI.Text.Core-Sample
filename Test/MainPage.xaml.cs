using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

using System.Diagnostics;
using Windows.UI;
using Windows.UI.Xaml.Documents;
using Windows.UI.Text.Core;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Core;
using Windows.UI.Core;
using Windows.UI.Text;
using System.Threading.Tasks;

// 空白ページのアイテム テンプレートについては、http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409 を参照してください

namespace Test
{
    /// <summary>
    /// それ自体で使用できる空白ページまたはフレーム内に移動できる空白ページ。
    /// </summary>
    public sealed partial class MainPage : Page
    {
        // IMEの操作は主にこのオブジェクトを使います。
        CoreTextEditContext editContext;

        // editContextを作成するのと、IMEの切り替えのイベントを受け取るのに使います。
        CoreTextServicesManager textServiceManager;

        // テキストの選択位置
        CoreTextRange Selection;

        // RunでIカーソルっぽいのを作ります。
        Run Cursor;

        // カーソルや文字を描画するブラシ
        SolidColorBrush BlackBrush  = new SolidColorBrush(Colors.Black);
        SolidColorBrush RedBrush    = new SolidColorBrush(Colors.Red);
        SolidColorBrush GreenBrush  = new SolidColorBrush(Colors.Green);
        SolidColorBrush BlueBrush   = new SolidColorBrush(Colors.Blue);

        /*
            コンストラクタ
        */
        public MainPage() {
            this.InitializeComponent();

            // テキストの選択位置を初期化します。
            Selection.StartCaretPosition = 0;
            Selection.EndCaretPosition = 0;

            // RunでIカーソルっぽいのを作ります。
            Cursor = new Run();
            Cursor.Text = "|";
            Cursor.Foreground = BlueBrush;

            // TextBlockの中にカーソルを入れます。
            EditText.Inlines.Add(Cursor);
        }

        /*
            ページがロードされた。
        */
        private void Page_Loaded(object sender, RoutedEventArgs e) {
            Debug.WriteLine("<<--- Page Loaded");

            // キーが押されたときのイベントハンドラを登録します。
            CoreApplication.GetCurrentView().CoreWindow.KeyDown += CoreWindow_KeyDown;
        }

        /*
            ボタンがフォーカスを取得した。
            TextBlockはフォーカスの取得/喪失の管理ができないので、TextBlockの上に透明のRadioButtonをかぶせてフォーカスの管理をしています。
        */
        private async void OverlappedButton_GotFocus(object sender, RoutedEventArgs e) {
            Debug.WriteLine("<<--- Button GotFocus");

            if (DesignMode.DesignModeEnabled) {
                // ビューデザイナーの中で動作している場合は何もしない。

                return;
            }

            // グリッドの外枠を青にしてフォーカスの取得を視覚的にユーザーに示します。
            MainGrid.BorderBrush = new SolidColorBrush(Colors.Blue);

            if(textServiceManager == null) {
                // 初めての場合

                // 少し待たないと「漢字」キー
                await Task.Delay(1000);

                // CoreTextServicesManagerを作ります。
                Debug.WriteLine("--->> GetForCurrentView");
                textServiceManager = CoreTextServicesManager.GetForCurrentView();

                // IMEの切り替えのイベントハンドラを登録します。
                Debug.WriteLine("--->> Subscribe InputLanguageChanged");
                textServiceManager.InputLanguageChanged += TextServiceManager_InputLanguageChanged;
            }

            // editContextを作り直します。
            UpdateEditContext();
        }

        /*
            ボタンがフォーカスを喪失した。
        */
        private void OverlappedButton_LostFocus(object sender, RoutedEventArgs e) {
            Debug.WriteLine("<<--- Button LostFocus");

            if (DesignMode.DesignModeEnabled) {
                // ビューデザイナーの中で動作している場合は何もしない。

                return;
            }

            // グリッドの外枠を灰色にしてフォーカスの喪失を視覚的にユーザーに示します。
            MainGrid.BorderBrush = new SolidColorBrush(Colors.Gray);

            if (editContext != null) {

                // IMEにフォーカスの喪失を知らせます。
                Debug.WriteLine("--->> NotifyFocusLeave");
                editContext.NotifyFocusLeave();
            }
        }

        /*
            editContextを作り直します。
        */
        void UpdateEditContext() {
            if (DesignMode.DesignModeEnabled) {
                // ビューデザイナーの中で動作している場合は何もしない。

                return;
            }

            // CoreTextEditContextオブジェクトを作ります。
            // IMEとのやりとりはこのオブジェクトを使います。
            Debug.WriteLine("--->> CreateEditContext");
            editContext = textServiceManager.CreateEditContext();

            // IMEの各種のイベントハンドラを登録します。
            Debug.WriteLine("--->> Subscribe IME Event");
            editContext.CompositionStarted          += EditContext_CompositionStarted;
            editContext.CompositionCompleted        += EditContext_CompositionCompleted;
            editContext.FocusRemoved                += EditContext_FocusRemoved;
            editContext.LayoutRequested             += EditContext_LayoutRequested;
            editContext.NotifyFocusLeaveCompleted   += EditContext_NotifyFocusLeaveCompleted;
            editContext.SelectionRequested          += EditContext_SelectionRequested;
            editContext.SelectionUpdating           += EditContext_SelectionUpdating;
            editContext.TextRequested               += EditContext_TextRequested;
            editContext.TextUpdating                += EditContext_TextUpdating;
            editContext.FormatUpdating              += EditContext_FormatUpdating;

            // IMEにフォーカスの取得を知らせます。
            Debug.WriteLine("--->> NotifyFocusEnter");
            editContext.NotifyFocusEnter();
        }

        /*
            IMEが切り替えられた。
        */
        private void TextServiceManager_InputLanguageChanged(CoreTextServicesManager sender, object ev) {
            Debug.Write("<<--- InputLanguageChanged");

            // IMEの名前を得ます。
            Windows.Globalization.Language lng = sender.InputLanguage;
            if (lng != null) {

                Debug.Write(" Lang:{0}", lng.DisplayName);
            }
            Debug.WriteLine("");

            // editContextを作り直します。
            UpdateEditContext();
        }

        /*
            カーソルがあると文字の操作に邪魔なので、いったん取り除きます。
        */
        void RemoveCursor() {
            EditText.Inlines.Remove(Cursor);
        }

        /*
            カーソルを挿入します。
        */
        void InsertCursor() {
            if (EditText.Inlines.Contains(Cursor)) {
                // すでにカーソルがある場合

                // カーソルを取り除きます。
                EditText.Inlines.Remove(Cursor);
            }

            // テキストの選択位置の最後にカーソルを挿入します。
            EditText.Inlines.Insert(Selection.EndCaretPosition, Cursor);
        }

        /*
            テキストの内容の変化を通知してきた。
        */
        private void EditContext_TextUpdating(CoreTextEditContext sender, CoreTextTextUpdatingEventArgs ev) {
            Debug.WriteLine("<<--- TextUpdating:({0},{1})->({2},{3}) [{4}] {5}",
                ev.Range.StartCaretPosition, ev.Range.EndCaretPosition,
                ev.NewSelection.StartCaretPosition, ev.NewSelection.EndCaretPosition,
                ev.Text,
                ev.Result
            );

            // カーソルがあると文字の操作に邪魔なので、いったん取り除きます。
            RemoveCursor();

            // 以前の選択位置の文字を末尾から取り除いていきます。
            for (int i = ev.Range.EndCaretPosition - 1; ev.Range.StartCaretPosition <= i; i--) {
                EditText.Inlines.RemoveAt(i);
            }

            // 新しいテキストを挿入します。
            for (int i = 0; i < ev.Text.Length; i++) {
                // １文字ごとにRunを作ります。
                Run txt = new Run();
                txt.Text = ev.Text.Substring(i, 1);

                // RunをTextBlockに挿入します。
                EditText.Inlines.Insert(ev.Range.StartCaretPosition + i, txt);
            }

            // アプリ内で持っているテキストの選択位置を更新します。
            Selection = ev.NewSelection;

            // カーソルを挿入します。
            InsertCursor();
        }

        /*
            テキストの選択位置の変化を通知してきた。
        */
        private void EditContext_SelectionUpdating(CoreTextEditContext sender, CoreTextSelectionUpdatingEventArgs ev) {
            Debug.WriteLine("<<--- SelectionUpdating: cancel:{0} result:{1} ({2},{3})",
                ev.IsCanceled,
                ev.Result,
                ev.Selection.StartCaretPosition, ev.Selection.EndCaretPosition
            );

            // カーソルがあると文字の操作に邪魔なので、いったん取り除きます。
            RemoveCursor();

            // アプリ内で持っているテキストの選択位置を更新します。
            Selection = ev.Selection;

            // カーソルを挿入します。
            InsertCursor();
        }

        /*
            テキストの選択位置を聞いてきた。
        */
        private void EditContext_SelectionRequested(CoreTextEditContext sender, CoreTextSelectionRequestedEventArgs ev) {
            Debug.WriteLine("<<--- SelectionRequested : {0}-{1}", Selection.StartCaretPosition, Selection.EndCaretPosition);

            // アプリ内で持っているテキストの選択位置を返します。
            ev.Request.Selection = Selection;
        }

        /*
            フォーカス喪失の通知が完了した。
            アプリからNotifyFocusLeaveを呼んだら、このメソッドが呼ばれます。
        */
        private void EditContext_NotifyFocusLeaveCompleted(CoreTextEditContext sender, object ev) {
            Debug.WriteLine("<<--- NotifyFocusLeaveCompleted");
        }

        /*
            TextBlock内の指定したindexまでのRunの幅を返します。

            How can I measure the Text Size in UWP Apps?
            http://stackoverflow.com/questions/35969056/how-can-i-measure-the-text-size-in-uwp-apps
        */
        double MeasureTextWidth(int index) {
            // indexまでのカーソル以外のRunの文字からテキストを作ります。
            string str = new string( (from x in EditText.Inlines where x != Cursor select ((Run)x).Text[0]).Take(index).ToArray() );

            // このテキストを含むTextBlockを作ります。
            var tb = new TextBlock { Text = str, FontSize = EditText.FontSize };

            // TextBlockのサイズを計算します。
            tb.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));

            // TextBlockの幅を返します。
            return tb.DesiredSize.Width;
        }

        /*
            入力コントロールの位置と入力テキストの位置を聞いてきた。
        */
        private void EditContext_LayoutRequested(CoreTextEditContext sender, CoreTextLayoutRequestedEventArgs ev) {
            Debug.WriteLine("<<--- LayoutRequested range:{0}-{1}", ev.Request.Range.StartCaretPosition, ev.Request.Range.EndCaretPosition);

            // メインウインドウを囲む矩形を得ます。
            Rect wnd_rect = CoreApplication.GetCurrentView().CoreWindow.Bounds;

            // TextBlockのページ内の位置を得ます。
            Point edit_pos = EditText.TransformToVisual(this).TransformPoint(new Point(0, 0));

            // TextBlockのスクリーン座標を計算します。
            double edit_screen_x = wnd_rect.X + edit_pos.X;
            double edit_screen_y = wnd_rect.Y + edit_pos.Y;

            // TextBlockを囲む矩形のスクリーン座標を返します。
            ev.Request.LayoutBounds.ControlBounds = new Rect(edit_screen_x, edit_screen_y, EditText.ActualWidth, EditText.ActualHeight);

            // TextBlock内の指定した位置までのRunの幅を得ます。
            double text_x = MeasureTextWidth(ev.Request.Range.EndCaretPosition);

            // 選択位置のテキストのスクリーン座標を計算します。
            double text_screen_x = edit_screen_x + text_x;

            // 選択範囲のテキストを囲む矩形をスクリーン座標で返します。
            ev.Request.LayoutBounds.TextBounds = new Rect(text_screen_x, edit_screen_y, 0, EditText.ActualHeight);
        }

        /*
            かな漢字変換の途中で表示するテキストの書式を指定してきた。
        */
        private void EditContext_FormatUpdating(CoreTextEditContext sender, CoreTextFormatUpdatingEventArgs ev) {
            Debug.WriteLine("<<--- FormatUpdating: BG:{0} cancel:{1} range:({2},{3}) reason:{4} result:{5} color:{6} under-line:({7},{8})",
                (ev.BackgroundColor == null ? "null" : ev.BackgroundColor.Value.ToString()),
                ev.IsCanceled,
                ev.Range.StartCaretPosition, ev.Range.EndCaretPosition,
                ev.Reason,
                ev.Result,
                (ev.TextColor == null ? "null" : ev.TextColor.Value.ToString()),
                (ev.UnderlineColor == null ? "null" : ev.UnderlineColor.Value.ToString()),
                (ev.UnderlineType == null ? "null" : ev.UnderlineType.Value.ToString())
            );

            // カーソルがあると文字の操作に邪魔なので、いったん取り除きます。
            RemoveCursor();

            // 選択範囲の文字の書式を設定します。
            for (int i = ev.Range.StartCaretPosition; i < ev.Range.EndCaretPosition; i++) {
                // 文字を含むRunを得ます。
                Run r = (Run)EditText.Inlines[i];

                // 下線の種類によってRunの色を変えます。 ( Runには下線のプロパティがないので。 )
                switch (ev.UnderlineType) {
                case UnderlineType.Wave:
                    r.Foreground = BlueBrush;
                    break;

                case UnderlineType.Thick:
                    r.Foreground = GreenBrush;
                    break;

                case UnderlineType.Thin:
                    r.Foreground = RedBrush;
                    break;

                case UnderlineType.None:
                case UnderlineType.Undefined:
                default:
                    r.Foreground = BlackBrush;
                    break;
                }                
            }

            // カーソルを挿入します。
            InsertCursor();
        }

        /*
            かな漢字変換を開始した。
        */
        private void EditContext_CompositionStarted(CoreTextEditContext sender, CoreTextCompositionStartedEventArgs ev) {
            Debug.WriteLine("<<--- CompositionStarted");
        }

        /*
            かな漢字変換が終わって入力テキストが確定した。
        */
        private void EditContext_CompositionCompleted(CoreTextEditContext sender, CoreTextCompositionCompletedEventArgs ev) {
            StringWriter sw = new StringWriter();

            // 文節ごとのテキスト位置と漢字の読みを得ます。
            foreach (CoreTextCompositionSegment seg in ev.CompositionSegments) {
                sw.Write("({0},{1}):{2} ", seg.Range.StartCaretPosition, seg.Range.EndCaretPosition, seg.PreconversionString);
            }

            Debug.WriteLine("<<--- CompositionCompleted:{0} {1}", ev.IsCanceled, sw.ToString());
        }

        /*
            アプリ内で持っているテキストが欲しいと言ってきた。
            CoreTextEditContextを作るとこれが呼ばれます。
        */
        private void EditContext_TextRequested(CoreTextEditContext sender, CoreTextTextRequestedEventArgs ev) {
            // TextBlockの中のカーソル以外のRunの文字からテキストを作ります。
            ev.Request.Text = new string( (from x in EditText.Inlines where x != Cursor select ((Run)x).Text[0]).ToArray() );

            Debug.WriteLine("<<--- TextRequested : {0}-{1} [{2}]", ev.Request.Range.StartCaretPosition, ev.Request.Range.EndCaretPosition, ev.Request.Text);
        }

        /*
            IME側の理由で入力フォーカスがなくなった。
            これがどのタイミングで呼ばれるかは不明。
        */
        private void EditContext_FocusRemoved(CoreTextEditContext sender, object ev) {
            Debug.WriteLine("<<--- FocusRemoved");
        }

        /*
            キーが押された。
        */
        private void CoreWindow_KeyDown(CoreWindow sender, KeyEventArgs args) {
            Debug.WriteLine("<<--- Key Down {0}", args.VirtualKey);

            switch (args.VirtualKey) {
            case Windows.System.VirtualKey.Left: // 左矢印(←)キー

                // カーソルがあると文字の操作に邪魔なので、いったん取り除きます。
                RemoveCursor();

                if (0 < Selection.EndCaretPosition) {
                    // テキストの選択位置が左端でない場合

                    // テキストの選択位置を１つ左に移動します。
                    Selection.EndCaretPosition--;
                    Selection.StartCaretPosition = Selection.EndCaretPosition;

                    // IMEに選択位置の変更を知らせます。
                    Debug.WriteLine("--->> NotifySelectionChanged");
                    editContext.NotifySelectionChanged(Selection);
                }

                // カーソルを挿入します。
                InsertCursor();
                break;

            case Windows.System.VirtualKey.Right: // 右矢印(→)キー

                // カーソルがあると文字の操作に邪魔なので、いったん取り除きます。
                RemoveCursor();

                if (Selection.EndCaretPosition < EditText.Inlines.Count) {
                    // テキストの選択位置が右端でない場合

                    // テキストの選択位置を１つ右に移動します。
                    Selection.EndCaretPosition++;
                    Selection.StartCaretPosition = Selection.EndCaretPosition;

                    // IMEに選択位置の変更を知らせます。
                    Debug.WriteLine("--->> NotifySelectionChanged");
                    editContext.NotifySelectionChanged(Selection);
                }

                // カーソルを挿入します。
                InsertCursor();
                break;


            case Windows.System.VirtualKey.Delete: // Deleteキー

                // カーソルがあると文字の操作に邪魔なので、いったん取り除きます。
                RemoveCursor();

                if (Selection.EndCaretPosition < EditText.Inlines.Count) {
                    // テキストの選択位置が右端でない場合

                    // テキストの選択位置の文字を１つ削除します。
                    EditText.Inlines.RemoveAt(Selection.EndCaretPosition);

                    // 削除前のテキストの選択位置をセットします。
                    CoreTextRange modified_range;
                    modified_range.StartCaretPosition = Selection.EndCaretPosition;
                    modified_range.EndCaretPosition   = Selection.EndCaretPosition + 1;

                    // 削除後のテキストの選択位置をセットします。
                    Selection.StartCaretPosition = Selection.EndCaretPosition;

                    // IMEにテキストの変更を知らせます。
                    Debug.WriteLine("--->> NotifyTextChanged");
                    editContext.NotifyTextChanged(modified_range, 0, Selection);
                }

                // カーソルを挿入します。
                InsertCursor();
                break;
            }
        }
    }
}
