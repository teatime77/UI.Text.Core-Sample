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

using System.ComponentModel;
using System.Diagnostics;
using Windows.UI;
using Windows.UI.Xaml.Documents;
using Windows.UI.Text.Core;
using Windows.ApplicationModel;

// 空白ページのアイテム テンプレートについては、http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409 を参照してください

namespace Test
{
    /// <summary>
    /// それ自体で使用できる空白ページまたはフレーム内に移動できる空白ページ。
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private CoreTextServicesManager textServiceManager = null;
        CoreTextEditContext Ctx;
        CoreTextRange Selection;
        int CursorPos = 0;
        Run Cursor;
        bool IsReady = false;

        public MainPage() {
            this.InitializeComponent();

            this.Selection.StartCaretPosition = 0;
            this.Selection.EndCaretPosition = 0;

            Cursor = new Run();
            Cursor.Text = "I";
            Cursor.Foreground = new SolidColorBrush(Colors.Blue);

            EditText.Inlines.Add(Cursor);
        }

        private void OverlappedButton_Click(object sender, RoutedEventArgs e) {
            if (!IsReady) {

                IsReady = true;
                MainGrid.BorderBrush = new SolidColorBrush(Colors.Blue);
                UpdateEditContext();
            }
        }

        private void OverlappedButton_GotFocus(object sender, RoutedEventArgs e) {
            if (!IsReady) {
                return;
            }

            MainGrid.BorderBrush = new SolidColorBrush(Colors.Blue);
            Point pt1 = TransformToVisual(null).TransformPoint(new Point(0, 0));
            Point pt = EditText.TransformToVisual(this).TransformPoint(new Point(0, 0));
            Point pt2 = TransformToVisual(Window.Current.Content).TransformPoint(new Point(0, 0));

            Debug.WriteLine("Pos {0}", pt2);


            Debug.WriteLine("In MyEdit フォーカス");
            UpdateEditContext();
        }

        private void OverlappedButton_LostFocus(object sender, RoutedEventArgs e) {
            Debug.WriteLine("In MyEdit LostFocus");
            MainGrid.BorderBrush = new SolidColorBrush(Colors.Gray);
            if (Ctx != null) {
                Ctx.NotifyFocusLeave();
            }
        }


        void UpdateEditContext() {
            if (DesignMode.DesignModeEnabled) {
                return;
            }

            if (textServiceManager == null) {

                textServiceManager = CoreTextServicesManager.GetForCurrentView();
                textServiceManager.InputLanguageChanged += TextServiceManager_InputLanguageChanged;
            }
            Ctx = textServiceManager.CreateEditContext();

            Ctx.CompositionCompleted += Ctx_CompositionCompleted;
            Ctx.CompositionStarted += Ctx_CompositionStarted;
            Ctx.FocusRemoved += Ctx_FocusRemoved;
            Ctx.FormatUpdating += Ctx_FormatUpdating;
            Ctx.LayoutRequested += Ctx_LayoutRequested;
            Ctx.NotifyFocusLeaveCompleted += Ctx_NotifyFocusLeaveCompleted;
            Ctx.SelectionRequested += Ctx_SelectionRequested;
            Ctx.SelectionUpdating += Ctx_SelectionUpdating;
            Ctx.TextRequested += Ctx_TextRequested;
            Ctx.TextUpdating += Ctx_TextUpdating;

            Debug.WriteLine("Policy:{0} Scope:{1} Read:{2} Name:{3}",
                Ctx.InputPaneDisplayPolicy.ToString(),
                Ctx.InputScope.ToString(),
                Ctx.IsReadOnly.ToString(),
                (Ctx.Name == null ? "null" : Ctx.Name)
            );

            Ctx.NotifyFocusEnter();
        }

        private void TextServiceManager_InputLanguageChanged(CoreTextServicesManager sender, object ev) {
            Windows.Globalization.Language lng = sender.InputLanguage;
            if (lng != null) {

                Debug.WriteLine("Lang:{0}", lng.DisplayName);
            }
            Debug.WriteLine("Input Language Changed");

            UpdateEditContext();
        }

        void RemoveCursor() {
            EditText.Inlines.RemoveAt(CursorPos);
        }

        void InsertCursor(int pos) {
            CursorPos = pos;
            EditText.Inlines.Insert(CursorPos, Cursor);
        }

        private void Ctx_TextUpdating(CoreTextEditContext sender, CoreTextTextUpdatingEventArgs ev) {
            RemoveCursor();
            for (int i = ev.Range.EndCaretPosition - 1; ev.Range.StartCaretPosition <= i; i--) {
                EditText.Inlines.RemoveAt(i);
            }
            for (int i = 0; i < ev.Text.Length; i++) {
                Run txt = new Run();
                txt.Text = ev.Text.Substring(i, 1);
                EditText.Inlines.Insert(ev.Range.StartCaretPosition + i, txt);
            }

            this.Selection.StartCaretPosition = ev.Range.StartCaretPosition + ev.Text.Length;
            this.Selection.EndCaretPosition = this.Selection.StartCaretPosition;

            InsertCursor(this.Selection.EndCaretPosition);

            Debug.WriteLine("Text Updating:({0},{1})->({2},{3}) [{4}] {5}",
                ev.Range.StartCaretPosition, ev.Range.EndCaretPosition,
                ev.NewSelection.StartCaretPosition, ev.NewSelection.EndCaretPosition,
                ev.Text,
                ev.Result
            );
        }

        private void Ctx_SelectionUpdating(CoreTextEditContext sender, CoreTextSelectionUpdatingEventArgs ev) {
            this.Selection = ev.Selection;
            RemoveCursor();
            InsertCursor(this.Selection.EndCaretPosition);

            Debug.WriteLine("Selection Updating: cancel:{0} result:{1} ({2},{3})",
                ev.IsCanceled,
                ev.Result,
                ev.Selection.StartCaretPosition, ev.Selection.EndCaretPosition
            );
        }

        private void Ctx_SelectionRequested(CoreTextEditContext sender, CoreTextSelectionRequestedEventArgs ev) {
            ev.Request.Selection = this.Selection;

            Debug.WriteLine("SelectionRequested : {0}-{1}", this.Selection.StartCaretPosition, this.Selection.EndCaretPosition);
        }

        private void Ctx_NotifyFocusLeaveCompleted(CoreTextEditContext sender, object ev) {
            Debug.WriteLine("NotifyFocusLeaveCompleted");
        }

        private void Ctx_LayoutRequested(CoreTextEditContext sender, CoreTextLayoutRequestedEventArgs ev) {
            ev.Request.LayoutBounds.ControlBounds = new Rect(0, 0, 100, 100);
            ev.Request.LayoutBounds.TextBounds = new Rect(0, 0, 100, 100);

            //Point pt1 = thePage.TransformToVisual(null).TransformPoint(new Point(0, 0));
            //Point pt = txt_Code.TransformToVisual(thePage).TransformPoint(new Point(0, 0));
            //Point pt2 = thePage.TransformToVisual(Window.Current.Content).TransformPoint(new Point(0, 0));

            Debug.WriteLine("LayoutRequested");
        }

        private void Ctx_FormatUpdating(CoreTextEditContext sender, CoreTextFormatUpdatingEventArgs ev) {
            Debug.WriteLine("Format Updating: BG:{0} cancel:{1} range:({2},{3}) reason:{4} result:{5} color:{6} under-line:({7},{8})",
                (ev.BackgroundColor == null ? "null" : ev.BackgroundColor.Value.ToString()),
                ev.IsCanceled,
                ev.Range.StartCaretPosition, ev.Range.EndCaretPosition,
                ev.Reason,
                ev.Result,
                (ev.TextColor == null ? "null" : ev.TextColor.Value.ToString()),
                (ev.UnderlineColor == null ? "null" : ev.UnderlineColor.Value.ToString()),
                (ev.UnderlineType == null ? "null" : ev.UnderlineType.Value.ToString())
            );
        }

        private void Ctx_FocusRemoved(CoreTextEditContext sender, object ev) {
            Debug.WriteLine("FocusRemoved");
        }

        private void Ctx_CompositionCompleted(CoreTextEditContext sender, CoreTextCompositionCompletedEventArgs ev) {
            StringWriter sw = new StringWriter();

            foreach (CoreTextCompositionSegment seg in ev.CompositionSegments) {
                sw.Write("({0},{1}):{2} ", seg.Range.StartCaretPosition, seg.Range.EndCaretPosition, seg.PreconversionString);
            }

            Debug.WriteLine("CompositionCompleted:{0} {1}", ev.IsCanceled, sw.ToString());
        }

        private void Ctx_CompositionStarted(CoreTextEditContext sender, CoreTextCompositionStartedEventArgs ev) {
            Debug.WriteLine("CompositionStarted");
        }

        private void Ctx_TextRequested(CoreTextEditContext sender, CoreTextTextRequestedEventArgs ev) {
            StringWriter sw = new StringWriter();

            foreach (Run r in EditText.Inlines) {
                if (r != Cursor) {
                    sw.Write(r.Text);
                }
            }
            ev.Request.Text = sw.ToString();

            Debug.WriteLine("Text Requested : {0}-{1} [{2}]", ev.Request.Range.StartCaretPosition, ev.Request.Range.EndCaretPosition, sw.ToString());
        }
    }
}
