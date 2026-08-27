using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FamilyTree.App.ViewModels;
using FamilyTree.Domain;

namespace FamilyTree.App.Controls;

/// <summary>
/// UserControl вводу неточної дати (T-5.2a, Частина 2). Наверх віддає одне значення —
/// DependencyProperty <see cref="Value"/> (<see cref="FamilyDate"/>?, двобічна за замовчуванням),
/// щоб редактори писали просто <c>Value="{Binding BirthDate}"</c>. Уся видима логіка (які поля
/// показувати, як зібрати/розібрати дату) — у <see cref="FamilyDateEditorViewModel"/>.
/// <para>
/// Синхронізація Value ↔ VM двобічна, тож потрібен ґард <see cref="_syncing"/> від відлуння:
/// зовні змінили Value → <see cref="OnValueChanged(FamilyDate?)"/> → <c>vm.LoadValue</c>;
/// користувач змінив поле → <c>vm.ValueChanged</c> → <c>SetCurrentValue(ValueProperty, vm.BuildValue())</c>.
/// </para>
/// <para>
/// DataContext ставиться на внутрішній <c>LayoutRoot</c>, а НЕ на сам UserControl: інакше він
/// перебив би успадкований DataContext, за яким споживач обчислює прив'язку <see cref="Value"/>.
/// Мова вбудованого DatePicker (парсинг дат) успадковується від вікна — його чіпляє UiLanguage.
/// </para>
/// </summary>
public partial class FamilyDateEditor : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(FamilyDate),
        typeof(FamilyDateEditor),
        new FrameworkPropertyMetadata(
            null,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnValuePropertyChanged));

    private readonly FamilyDateEditorViewModel _viewModel = new();

    // Поки true — сторони не реагують одна на одну (Value ↔ поля), щоб не було нескінченного відлуння.
    private bool _syncing;

    public FamilyDateEditor()
    {
        InitializeComponent();
        _viewModel.ValueChanged += ViewModel_ValueChanged;
        LayoutRoot.DataContext = _viewModel;
    }

    /// <summary>Поточна дата контрола. null — «не вказано».</summary>
    public FamilyDate? Value
    {
        get => (FamilyDate?)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    private static void OnValuePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((FamilyDateEditor)d).OnValueChanged((FamilyDate?)e.NewValue);

    private void OnValueChanged(FamilyDate? value)
    {
        if (_syncing)
        {
            return;
        }

        _syncing = true;
        try
        {
            _viewModel.LoadValue(value);
        }
        finally
        {
            _syncing = false;
        }
    }

    private void ViewModel_ValueChanged(object? sender, EventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        _syncing = true;
        try
        {
            SetCurrentValue(ValueProperty, _viewModel.BuildValue());
        }
        finally
        {
            _syncing = false;
        }
    }

    // Рік — лише цифри (набір з клавіатури). Довжину обмежує MaxLength у XAML.
    private void Year_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = !IsAllDigits(e.Text);

    // Рік — лише цифри (вставка з буфера): нецифровий вміст відхиляємо цілком.
    private void Year_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetDataPresent(DataFormats.Text)
            && e.DataObject.GetData(DataFormats.Text) is string text
            && IsAllDigits(text))
        {
            return;
        }

        e.CancelCommand();
    }

    private static bool IsAllDigits(string text) =>
        text.Length > 0 && text.All(char.IsDigit);
}
