using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Nova.Client.Shell;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// The ship designer's component-category browser (behavior-specs-10/client-ui-dialog-catalog.md
/// "Search and record browser": a list of component categories, and Prev/Next stepping through
/// the race's available components of the chosen category; position kept by
/// Nova.Client.Shell.CategoryStepper). Picking a category shows only that category's components
/// ("All categories" shows the whole palette as before); Prev/Next arm the stepped component,
/// ready to place in a slot, and move the category choice along when stepping crosses into
/// another category. Desktop only (ShipDesignView hides the strip on Android).
/// </summary>
public partial class ShipDesignViewModel
{
    public const string AllCategoriesLabel = "All categories";

    private CategoryStepper<ComponentListItemViewModel>? browser;

    private CategoryStepper<ComponentListItemViewModel> Browser =>
        browser ??= new CategoryStepper<ComponentListItemViewModel>(Categories.Select(category => category.Items));

    /// <summary>"All categories", then each category's name.</summary>
    public IReadOnlyList<string> BrowseCategoryNames =>
        new[] { AllCategoriesLabel }.Concat(Categories.Select(category => category.Name)).ToList();

    private int selectedBrowseCategoryIndex;

    /// <summary>0 = all categories, otherwise category index + 1.</summary>
    public int SelectedBrowseCategoryIndex
    {
        get => selectedBrowseCategoryIndex;
        set
        {
            if (value < 0 || value > Categories.Count)
            {
                return;
            }

            if (SetProperty(ref selectedBrowseCategoryIndex, value))
            {
                if (value > 0 && Browser.CategoryIndex != value - 1)
                {
                    Browser.SelectCategory(value - 1);
                }

                OnPropertyChanged(nameof(VisibleCategories));
                OnPropertyChanged(nameof(BrowsePositionText));
            }
        }
    }

    /// <summary>The palette's categories under the current choice.</summary>
    public IReadOnlyList<ComponentCategoryViewModel> VisibleCategories =>
        selectedBrowseCategoryIndex == 0
            ? Categories
            : Categories.Skip(selectedBrowseCategoryIndex - 1).Take(1).ToList();

    /// <summary>"Category: n of m - Name" for the stepped component.</summary>
    public string BrowsePositionText
    {
        get
        {
            if (!Browser.HasCurrent)
            {
                return "No components available";
            }

            ComponentCategoryViewModel category = Categories[Browser.CategoryIndex];
            return $"{category.Name}: {Browser.EntryIndex + 1} of {category.Items.Count} - {Browser.Current!.Name}";
        }
    }

    private IRelayCommand? browseNextCommand;

    public IRelayCommand BrowseNextCommand => browseNextCommand ??= new RelayCommand(() => Step(next: true));

    private IRelayCommand? browsePreviousCommand;

    public IRelayCommand BrowsePreviousCommand => browsePreviousCommand ??= new RelayCommand(() => Step(next: false));

    private void Step(bool next)
    {
        CategoryStepper<ComponentListItemViewModel> stepper = Browser;
        if (!stepper.HasCurrent)
        {
            return;
        }

        // Start from the component already armed, if it is one of the palette's.
        if (armedItem != null)
        {
            stepper.Select(armedItem);
        }

        if (next)
        {
            stepper.Next();
        }
        else
        {
            stepper.Previous();
        }

        ComponentListItemViewModel? item = stepper.Current;
        if (item == null)
        {
            return;
        }

        if (selectedBrowseCategoryIndex != 0)
        {
            SelectedBrowseCategoryIndex = stepper.CategoryIndex + 1;
        }

        if (!ReferenceEquals(armedItem, item))
        {
            ArmComponent(item);
        }

        OnPropertyChanged(nameof(BrowsePositionText));
    }
}
