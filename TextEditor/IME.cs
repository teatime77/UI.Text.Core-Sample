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

namespace MyEdit {
    public partial class MyEditor {
        // IMEの操作は主にこのオブジェクトを使います。
        CoreTextEditContext editContext;

        // editContextを作成するのと、IMEの切り替えのイベントを受け取るのに使います。
        CoreTextServicesManager textServiceManager;

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

            if (textServiceManager == null) {
                // 初めての場合

                // 少し待たないと「漢字」キーが効かない。
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

            Win2DCanvas.Invalidate();
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

            if (editContext != null) {

                // IMEにフォーカスの喪失を知らせます。
                Debug.WriteLine("--->> NotifyFocusLeave");
                editContext.NotifyFocusLeave();
            }

            Win2DCanvas.Invalidate();
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
            テキストの内容の変化を通知してきた。
        */
        private void EditContext_TextUpdating(CoreTextEditContext sender, CoreTextTextUpdatingEventArgs ev) {
            Debug.WriteLine("<<--- TextUpdating:({0},{1})->({2},{3}) [{4}] {5} {6}",
                ev.Range.StartCaretPosition, ev.Range.EndCaretPosition,
                ev.NewSelection.StartCaretPosition, ev.NewSelection.EndCaretPosition,
                ev.Text,
                ev.Result,
                MeasureText(ev.Text, TextFormat)
            );

            // 以前の選択位置の文字を末尾から取り除いていきます。
            for (int i = ev.Range.EndCaretPosition - 1; ev.Range.StartCaretPosition <= i; i--) {
                Chars.RemoveAt(i);
            }

            // 新しいテキストを挿入します。
            for (int i = 0; i < ev.Text.Length; i++) {
                Chars.Insert(ev.Range.StartCaretPosition + i, new TChar(ev.Text[i]));
            }

            // アプリ内で持っているテキストの選択位置を更新します。
            SelOrigin  = ev.Range.StartCaretPosition  + ev.Text.Length;
            SelCurrent = SelOrigin;

            Win2DCanvas.Invalidate();
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

            // アプリ内で持っているテキストの選択位置を更新します。
            SelOrigin = ev.Selection.StartCaretPosition;
            SelCurrent = ev.Selection.EndCaretPosition;
        }

        /*
            テキストの選択位置を聞いてきた。
        */
        private void EditContext_SelectionRequested(CoreTextEditContext sender, CoreTextSelectionRequestedEventArgs ev) {
            CoreTextRange rng;
            rng.StartCaretPosition = SelStart;
            rng.EndCaretPosition = SelEnd;

            Debug.WriteLine("<<--- SelectionRequested : {0}-{1}", rng.StartCaretPosition, rng.EndCaretPosition);

            // アプリ内で持っているテキストの選択位置を返します。
            ev.Request.Selection = rng;
        }

        /*
            フォーカス喪失の通知が完了した。
            アプリからNotifyFocusLeaveを呼んだら、このメソッドが呼ばれます。
        */
        private void EditContext_NotifyFocusLeaveCompleted(CoreTextEditContext sender, object ev) {
            Debug.WriteLine("<<--- NotifyFocusLeaveCompleted");
        }

        /*
            入力コントロールの位置と入力テキストの位置を聞いてきた。
        */
        private void EditContext_LayoutRequested(CoreTextEditContext sender, CoreTextLayoutRequestedEventArgs ev) {
            Debug.WriteLine("<<--- LayoutRequested range:{0}-{1}", ev.Request.Range.StartCaretPosition, ev.Request.Range.EndCaretPosition);


            // メインウインドウを囲む矩形を得ます。
            Rect wnd_rect = CoreApplication.GetCurrentView().CoreWindow.Bounds;

            // Canvasのページ内の位置を得ます。
            FrameworkElement root = this;
            for (root = this; root.Parent is FrameworkElement;) {
                root = root.Parent as FrameworkElement;
            }

            Point edit_pos = TransformToVisual(root).TransformPoint(new Point(0, 0));

            // Canvasのスクリーン座標を計算します。
            double edit_screen_x = wnd_rect.X + edit_pos.X;
            double edit_screen_y = wnd_rect.Y + edit_pos.Y;

            // Canvasを囲む矩形のスクリーン座標を返します。
            Rect canvas_rect = new Rect(edit_screen_x, edit_screen_y, Win2DCanvas.ActualWidth, Win2DCanvas.ActualHeight);
            ev.Request.LayoutBounds.ControlBounds = canvas_rect;

            // 選択位置の語句を描画した図形または直前の図形のリストを得ます。(選択位置が文書の末尾にある場合は直前の図形を使います。)
            var draw_list = from x in DrawList where x.StartPos <= SelCurrent && SelCurrent <= x.EndPos select x;
            if (draw_list.Any()) {
                // 図形を得られた場合

                // 語句を描画した図形を得ます。
                TShape phrase_shape = draw_list.First();

                // 描画した語句の先頭から選択位置までのテキストのサイズを得ます。
                Size sz = MeasureText(StringFromRange(phrase_shape.StartPos, SelCurrent), TextFormat);

                // 選択位置のテキストを囲む矩形を計算します。
                Rect text_rect;
                text_rect.X = canvas_rect.X + phrase_shape.Bounds.X + sz.Width;
                text_rect.Y = canvas_rect.Y + phrase_shape.Bounds.Y;
                text_rect.Width = SpaceWidth;
                text_rect.Height = sz.Height;

                // 選択範囲のテキストを囲む矩形をスクリーン座標で返します。
                ev.Request.LayoutBounds.TextBounds = text_rect;
            }
            else {
                // 図形を得られない場合

                // 選択範囲のテキストを囲む矩形はCanvasを囲む矩形とします。
                ev.Request.LayoutBounds.TextBounds = canvas_rect;
            }

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

            for (int i = ev.Range.StartCaretPosition; i < ev.Range.EndCaretPosition; i++) {

                if (ev.UnderlineType != null) {
                    Chars[i].Underline = ev.UnderlineType.Value;
                }
            }

            Win2DCanvas.Invalidate();
        }

        /*
            かな漢字変換を開始した。
        */
        private void EditContext_CompositionStarted(CoreTextEditContext sender, CoreTextCompositionStartedEventArgs ev) {
            Debug.WriteLine("<<--- CompositionStarted");
            InComposition = true;
        }

        /*
            かな漢字変換が終わって入力テキストが確定した。
        */
        private void EditContext_CompositionCompleted(CoreTextEditContext sender, CoreTextCompositionCompletedEventArgs ev) {
            InComposition = false;
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
            ev.Request.Text = CurrentLineString();

            Debug.WriteLine("<<--- TextRequested : {0}-{1}", ev.Request.Range.StartCaretPosition, ev.Request.Range.EndCaretPosition);
        }

        /*
            IME側の理由で入力フォーカスがなくなった。
            これがどのタイミングで呼ばれるかは不明。
        */
        private void EditContext_FocusRemoved(CoreTextEditContext sender, object ev) {
            Debug.WriteLine("<<--- FocusRemoved");
        }

        /*
            アプリ内のテキストの選択位置の変更をIMEに伝えます。
        */
        void MyNotifySelectionChanged() {
            CoreTextRange new_range;
            new_range.StartCaretPosition = SelStart;
            new_range.EndCaretPosition = SelEnd;

            Debug.WriteLine("--->> NotifySelectionChanged");
            editContext.NotifySelectionChanged(new_range);
        }
    }
}