namespace FamilyTree.App.ViewModels;

/// <summary>Відповідь на запит «Так / Ні / Пізніше».</summary>
public enum ThreeWayChoice
{
    /// <summary>Так — виконати дію.</summary>
    Yes,

    /// <summary>Ні — цей варіант не підходить (можна запитати про наступний).</summary>
    No,

    /// <summary>Пізніше — відкласти рішення й припинити поточне опитування.</summary>
    Later,
}

/// <summary>
/// ViewModel простого діалогу з трьома відповідями. Потрібен там, де стандартний
/// MessageBox не годиться: у WPF його кнопки не перепідписати, а «Ні» та «Пізніше»
/// мають різний сенс.
/// </summary>
public sealed class ChoiceViewModel
{
    public ChoiceViewModel(string title, string message)
    {
        Title = title;
        Message = message;
    }

    public string Title { get; }

    public string Message { get; }

    /// <summary>Вибір користувача (типово — «пізніше», тобто закриття вікна нічого не змінює).</summary>
    public ThreeWayChoice Choice { get; set; } = ThreeWayChoice.Later;
}
