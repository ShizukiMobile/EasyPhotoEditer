using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace EasyPhotoEditer
{
    public class TransformEventArgs : EventArgs
    {
        public Rect Before { get; }
        public Rect After { get; }

        public TransformEventArgs(Rect before, Rect after)
        {
            Before = before;
            After = after;
        }
    }

    public partial class EditableItem : UserControl
    {
        private const double MinSize = 16.0;

        private bool _isDragging;
        private Point _dragStartPoint;
        private double _originalLeft;
        private double _originalTop;
        private Rect _dragStartRect;
        private Rect _resizeStartRect;
        private bool _isSelected;
        private bool _isTextBox;

        public event EventHandler? Selected;
        public event EventHandler<TransformEventArgs>? TransformCompleted;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                SelectionOverlay.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        public bool IsTextBox
        {
            get => _isTextBox;
            set
            {
                _isTextBox = value;
                if (value)
                {
                    // テキストボックスの場合は中央をクリックして入力できるよう枠線のみを移動判定にする
                    MoveBorder.Background = null;
                    MoveBorder.BorderThickness = new Thickness(8);
                    MoveBorder.BorderBrush = Brushes.Transparent;
                }
                else
                {
                    // 通常図形は全体をクリック・ドラッグ可能
                    MoveBorder.Background = Brushes.Transparent;
                    MoveBorder.BorderThickness = new Thickness(0);
                    MoveBorder.BorderBrush = null;
                }
            }
        }

        public object? ItemContent
        {
            get => ContentHost.Content;
            set => ContentHost.Content = value;
        }

        public EditableItem()
        {
            InitializeComponent();
            IsTextBox = false;
        }

        public Rect GetCurrentRect()
        {
            double left = Canvas.GetLeft(this);
            if (double.IsNaN(left)) left = 0;
            double top = Canvas.GetTop(this);
            if (double.IsNaN(top)) top = 0;
            double width = ActualWidth > 0 ? ActualWidth : Width;
            double height = ActualHeight > 0 ? ActualHeight : Height;
            return new Rect(left, top, width, height);
        }

        private void UserControl_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Selected?.Invoke(this, EventArgs.Empty);
        }

        private void MoveBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Selected?.Invoke(this, EventArgs.Empty);
            Focus();

            if (Parent is Canvas canvas)
            {
                _isDragging = true;
                _dragStartPoint = e.GetPosition(canvas);
                _dragStartRect = GetCurrentRect();
                _originalLeft = _dragStartRect.X;
                _originalTop = _dragStartRect.Y;

                MoveBorder.CaptureMouse();
                e.Handled = true;
            }
        }

        private void MoveBorder_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging && Parent is Canvas canvas)
            {
                Point currentPos = e.GetPosition(canvas);
                double deltaX = currentPos.X - _dragStartPoint.X;
                double deltaY = currentPos.Y - _dragStartPoint.Y;

                Canvas.SetLeft(this, _originalLeft + deltaX);
                Canvas.SetTop(this, _originalTop + deltaY);
                e.Handled = true;
            }
        }

        private void MoveBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                MoveBorder.ReleaseMouseCapture();

                var endRect = GetCurrentRect();
                if (Math.Abs(endRect.X - _dragStartRect.X) > 0.5 || Math.Abs(endRect.Y - _dragStartRect.Y) > 0.5)
                {
                    TransformCompleted?.Invoke(this, new TransformEventArgs(_dragStartRect, endRect));
                }

                e.Handled = true;
            }
        }

        #region リサイズハンドル処理 (Shiftキーでの縦横比維持対応 & Undo/Redo通知)

        private void Thumb_DragStarted(object sender, DragStartedEventArgs e)
        {
            _resizeStartRect = GetCurrentRect();
        }

        private void Thumb_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            var endRect = GetCurrentRect();
            if (Math.Abs(endRect.X - _resizeStartRect.X) > 0.5 ||
                Math.Abs(endRect.Y - _resizeStartRect.Y) > 0.5 ||
                Math.Abs(endRect.Width - _resizeStartRect.Width) > 0.5 ||
                Math.Abs(endRect.Height - _resizeStartRect.Height) > 0.5)
            {
                TransformCompleted?.Invoke(this, new TransformEventArgs(_resizeStartRect, endRect));
            }
        }

        private void Thumb_TopLeft_DragDelta(object sender, DragDeltaEventArgs e)
        {
            double curW = ActualWidth > 0 ? ActualWidth : Width;
            double curH = ActualHeight > 0 ? ActualHeight : Height;
            double curLeft = Canvas.GetLeft(this);
            if (double.IsNaN(curLeft)) curLeft = 0;
            double curTop = Canvas.GetTop(this);
            if (double.IsNaN(curTop)) curTop = 0;

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && curH > 0)
            {
                double ratio = curW / curH;
                double deltaX = -e.HorizontalChange;
                double deltaY = -e.VerticalChange;

                double newW, newH;
                if (Math.Abs(deltaX) > Math.Abs(deltaY))
                {
                    newW = Math.Max(MinSize, curW + deltaX);
                    newH = Math.Max(MinSize, newW / ratio);
                    newW = newH * ratio;
                }
                else
                {
                    newH = Math.Max(MinSize, curH + deltaY);
                    newW = Math.Max(MinSize, newH * ratio);
                    newH = newW / ratio;
                }

                Canvas.SetLeft(this, curLeft + (curW - newW));
                Canvas.SetTop(this, curTop + (curH - newH));
                Width = newW;
                Height = newH;
            }
            else
            {
                ResizeTop(e.VerticalChange);
                ResizeLeft(e.HorizontalChange);
            }
        }

        private void Thumb_TopRight_DragDelta(object sender, DragDeltaEventArgs e)
        {
            double curW = ActualWidth > 0 ? ActualWidth : Width;
            double curH = ActualHeight > 0 ? ActualHeight : Height;
            double curTop = Canvas.GetTop(this);
            if (double.IsNaN(curTop)) curTop = 0;

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && curH > 0)
            {
                double ratio = curW / curH;
                double deltaX = e.HorizontalChange;
                double deltaY = -e.VerticalChange;

                double newW, newH;
                if (Math.Abs(deltaX) > Math.Abs(deltaY))
                {
                    newW = Math.Max(MinSize, curW + deltaX);
                    newH = Math.Max(MinSize, newW / ratio);
                    newW = newH * ratio;
                }
                else
                {
                    newH = Math.Max(MinSize, curH + deltaY);
                    newW = Math.Max(MinSize, newH * ratio);
                    newH = newW / ratio;
                }

                Canvas.SetTop(this, curTop + (curH - newH));
                Width = newW;
                Height = newH;
            }
            else
            {
                ResizeTop(e.VerticalChange);
                ResizeRight(e.HorizontalChange);
            }
        }

        private void Thumb_BottomLeft_DragDelta(object sender, DragDeltaEventArgs e)
        {
            double curW = ActualWidth > 0 ? ActualWidth : Width;
            double curH = ActualHeight > 0 ? ActualHeight : Height;
            double curLeft = Canvas.GetLeft(this);
            if (double.IsNaN(curLeft)) curLeft = 0;

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && curH > 0)
            {
                double ratio = curW / curH;
                double deltaX = -e.HorizontalChange;
                double deltaY = e.VerticalChange;

                double newW, newH;
                if (Math.Abs(deltaX) > Math.Abs(deltaY))
                {
                    newW = Math.Max(MinSize, curW + deltaX);
                    newH = Math.Max(MinSize, newW / ratio);
                    newW = newH * ratio;
                }
                else
                {
                    newH = Math.Max(MinSize, curH + deltaY);
                    newW = Math.Max(MinSize, newH * ratio);
                    newH = newW / ratio;
                }

                Canvas.SetLeft(this, curLeft + (curW - newW));
                Width = newW;
                Height = newH;
            }
            else
            {
                ResizeBottom(e.VerticalChange);
                ResizeLeft(e.HorizontalChange);
            }
        }

        private void Thumb_BottomRight_DragDelta(object sender, DragDeltaEventArgs e)
        {
            double curW = ActualWidth > 0 ? ActualWidth : Width;
            double curH = ActualHeight > 0 ? ActualHeight : Height;

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && curH > 0)
            {
                double ratio = curW / curH;
                double deltaX = e.HorizontalChange;
                double deltaY = e.VerticalChange;

                double newW, newH;
                if (Math.Abs(deltaX) > Math.Abs(deltaY))
                {
                    newW = Math.Max(MinSize, curW + deltaX);
                    newH = Math.Max(MinSize, newW / ratio);
                    newW = newH * ratio;
                }
                else
                {
                    newH = Math.Max(MinSize, curH + deltaY);
                    newW = Math.Max(MinSize, newH * ratio);
                    newH = newW / ratio;
                }

                Width = newW;
                Height = newH;
            }
            else
            {
                ResizeBottom(e.VerticalChange);
                ResizeRight(e.HorizontalChange);
            }
        }

        private void Thumb_Top_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                double curW = ActualWidth > 0 ? ActualWidth : Width;
                double curH = ActualHeight > 0 ? ActualHeight : Height;
                double curLeft = Canvas.GetLeft(this);
                if (double.IsNaN(curLeft)) curLeft = 0;
                double curTop = Canvas.GetTop(this);
                if (double.IsNaN(curTop)) curTop = 0;

                if (curH > 0)
                {
                    double ratio = curW / curH;
                    double newH = Math.Max(MinSize, curH - e.VerticalChange);
                    double newW = Math.Max(MinSize, newH * ratio);
                    newH = newW / ratio;

                    Canvas.SetLeft(this, curLeft - (newW - curW) / 2);
                    Canvas.SetTop(this, curTop + (curH - newH));
                    Width = newW;
                    Height = newH;
                    return;
                }
            }

            ResizeTop(e.VerticalChange);
        }

        private void Thumb_Bottom_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                double curW = ActualWidth > 0 ? ActualWidth : Width;
                double curH = ActualHeight > 0 ? ActualHeight : Height;
                double curLeft = Canvas.GetLeft(this);
                if (double.IsNaN(curLeft)) curLeft = 0;

                if (curH > 0)
                {
                    double ratio = curW / curH;
                    double newH = Math.Max(MinSize, curH + e.VerticalChange);
                    double newW = Math.Max(MinSize, newH * ratio);
                    newH = newW / ratio;

                    Canvas.SetLeft(this, curLeft - (newW - curW) / 2);
                    Width = newW;
                    Height = newH;
                    return;
                }
            }

            ResizeBottom(e.VerticalChange);
        }

        private void Thumb_Left_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                double curW = ActualWidth > 0 ? ActualWidth : Width;
                double curH = ActualHeight > 0 ? ActualHeight : Height;
                double curLeft = Canvas.GetLeft(this);
                if (double.IsNaN(curLeft)) curLeft = 0;
                double curTop = Canvas.GetTop(this);
                if (double.IsNaN(curTop)) curTop = 0;

                if (curH > 0)
                {
                    double ratio = curW / curH;
                    double newW = Math.Max(MinSize, curW - e.HorizontalChange);
                    double newH = Math.Max(MinSize, newW / ratio);
                    newH = newW / ratio;

                    Canvas.SetLeft(this, curLeft + (curW - newW));
                    Canvas.SetTop(this, curTop - (newH - curH) / 2);
                    Width = newW;
                    Height = newH;
                    return;
                }
            }

            ResizeLeft(e.HorizontalChange);
        }

        private void Thumb_Right_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                double curW = ActualWidth > 0 ? ActualWidth : Width;
                double curH = ActualHeight > 0 ? ActualHeight : Height;
                double curTop = Canvas.GetTop(this);
                if (double.IsNaN(curTop)) curTop = 0;

                if (curH > 0)
                {
                    double ratio = curW / curH;
                    double newW = Math.Max(MinSize, curW + e.HorizontalChange);
                    double newH = Math.Max(MinSize, newW / ratio);
                    newH = newW / ratio;

                    Canvas.SetTop(this, curTop - (newH - curH) / 2);
                    Width = newW;
                    Height = newH;
                    return;
                }
            }

            ResizeRight(e.HorizontalChange);
        }

        private void ResizeLeft(double horizontalChange)
        {
            double currentLeft = Canvas.GetLeft(this);
            if (double.IsNaN(currentLeft)) currentLeft = 0;
            double currentWidth = ActualWidth > 0 ? ActualWidth : Width;

            double newWidth = Math.Max(MinSize, currentWidth - horizontalChange);
            double deltaWidth = currentWidth - newWidth;

            Canvas.SetLeft(this, currentLeft + deltaWidth);
            Width = newWidth;
        }

        private void ResizeRight(double horizontalChange)
        {
            double currentWidth = ActualWidth > 0 ? ActualWidth : Width;
            Width = Math.Max(MinSize, currentWidth + horizontalChange);
        }

        private void ResizeTop(double verticalChange)
        {
            double currentTop = Canvas.GetTop(this);
            if (double.IsNaN(currentTop)) currentTop = 0;
            double currentHeight = ActualHeight > 0 ? ActualHeight : Height;

            double newHeight = Math.Max(MinSize, currentHeight - verticalChange);
            double deltaHeight = currentHeight - newHeight;

            Canvas.SetTop(this, currentTop + deltaHeight);
            Height = newHeight;
        }

        private void ResizeBottom(double verticalChange)
        {
            double currentHeight = ActualHeight > 0 ? ActualHeight : Height;
            Height = Math.Max(MinSize, currentHeight + verticalChange);
        }

        #endregion
    }
}
