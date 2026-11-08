using System.Collections.ObjectModel;
using System.Windows.Media;
using VoxPad.Core.Models;

namespace VoxPad.App.ViewModels;

public sealed class CategoryViewModel : ObservableObject
{
    private bool _isSelected;
    private bool _isExpanded;

    public Category Model { get; }
    public bool IsAllSounds { get; }
    public ObservableCollection<CategoryViewModel> Children { get; } = new();

    public CategoryViewModel(Category model, bool isAllSounds = false)
    {
        Model = model;
        IsAllSounds = isAllSounds;
        _isExpanded = model.Expanded;
        foreach (var c in model.Children) Children.Add(new CategoryViewModel(c));
    }

    public Guid Id => Model.Id;
    public string Name => IsAllSounds ? Services.Strings.Get("AllSounds", "All sounds") : Model.Name;
    public int Count => Model.SoundIds.Count;
    public string RandomHotkeyText => Model.RandomHotkey?.ToString() ?? "";
    public Brush? ColorBrush => Model.Color == 0 ? null : SoundItemViewModel.BrushFor(Model.Color);
    /// <summary>"All sounds" is hidden from the data model's point of view (it defines the index) but always visible in the tree.</summary>
    public bool IsHidden => Model.Hidden && !IsAllSounds;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public bool IsExpanded { get => _isExpanded; set { if (Set(ref _isExpanded, value)) Model.Expanded = value; } }

    public IEnumerable<CategoryViewModel> Flatten()
    {
        yield return this;
        foreach (var c in Children) foreach (var d in c.Flatten()) yield return d;
    }

    public void Refresh() { RaiseAll(); foreach (var c in Children) c.Refresh(); }
}
