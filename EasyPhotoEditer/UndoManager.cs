using System.Windows;
using System.Windows.Controls;

namespace EasyPhotoEditer
{
    public interface IUndoableAction
    {
        void Undo();
        void Redo();
    }

    public class UndoManager
    {
        private readonly Stack<IUndoableAction> _undoStack = new();
        private readonly Stack<IUndoableAction> _redoStack = new();

        public event EventHandler? StateChanged;

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        public void Record(IUndoableAction action)
        {
            _undoStack.Push(action);
            _redoStack.Clear();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Undo()
        {
            if (!CanUndo) return;
            var action = _undoStack.Pop();
            action.Undo();
            _redoStack.Push(action);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Redo()
        {
            if (!CanRedo) return;
            var action = _redoStack.Pop();
            action.Redo();
            _undoStack.Push(action);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public class AddObjectAction : IUndoableAction
    {
        private readonly Canvas _canvas;
        private readonly EditableItem _item;
        private readonly Action<EditableItem?> _selectItem;

        public AddObjectAction(Canvas canvas, EditableItem item, Action<EditableItem?> selectItem)
        {
            _canvas = canvas;
            _item = item;
            _selectItem = selectItem;
        }

        public void Undo()
        {
            _canvas.Children.Remove(_item);
            _selectItem(null);
        }

        public void Redo()
        {
            if (!_canvas.Children.Contains(_item))
            {
                _canvas.Children.Add(_item);
            }
            _selectItem(_item);
        }
    }

    public class DeleteObjectAction : IUndoableAction
    {
        private readonly Canvas _canvas;
        private readonly EditableItem _item;
        private readonly Action<EditableItem?> _selectItem;

        public DeleteObjectAction(Canvas canvas, EditableItem item, Action<EditableItem?> selectItem)
        {
            _canvas = canvas;
            _item = item;
            _selectItem = selectItem;
        }

        public void Undo()
        {
            if (!_canvas.Children.Contains(_item))
            {
                _canvas.Children.Add(_item);
            }
            _selectItem(_item);
        }

        public void Redo()
        {
            _canvas.Children.Remove(_item);
            _selectItem(null);
        }
    }

    public class TransformObjectAction : IUndoableAction
    {
        private readonly EditableItem _item;
        private readonly Rect _before;
        private readonly Rect _after;
        private readonly Action<EditableItem?> _selectItem;

        public TransformObjectAction(EditableItem item, Rect before, Rect after, Action<EditableItem?> selectItem)
        {
            _item = item;
            _before = before;
            _after = after;
            _selectItem = selectItem;
        }

        public void Undo()
        {
            ApplyRect(_before);
            _selectItem(_item);
        }

        public void Redo()
        {
            ApplyRect(_after);
            _selectItem(_item);
        }

        private void ApplyRect(Rect rect)
        {
            Canvas.SetLeft(_item, rect.X);
            Canvas.SetTop(_item, rect.Y);
            _item.Width = rect.Width;
            _item.Height = rect.Height;
        }
    }

    public class TextChangedAction : IUndoableAction
    {
        private readonly TextBox _textBox;
        private readonly string _before;
        private readonly string _after;

        public TextChangedAction(TextBox textBox, string before, string after)
        {
            _textBox = textBox;
            _before = before;
            _after = after;
        }

        public void Undo()
        {
            _textBox.Text = _before;
        }

        public void Redo()
        {
            _textBox.Text = _after;
        }
    }

    public class StyleChangeAction : IUndoableAction
    {
        private readonly Action _applyBefore;
        private readonly Action _applyAfter;

        public StyleChangeAction(Action applyBefore, Action applyAfter)
        {
            _applyBefore = applyBefore;
            _applyAfter = applyAfter;
        }

        public void Undo() => _applyBefore();
        public void Redo() => _applyAfter();
    }
}
