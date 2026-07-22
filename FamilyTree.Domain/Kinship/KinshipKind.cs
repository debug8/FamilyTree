namespace FamilyTree.Domain.Kinship;

/// <summary>Тип родинного зв'язку між кореневою особою та родичем.</summary>
public enum KinshipKind
{
    /// <summary>Це та сама особа.</summary>
    SamePerson,

    /// <summary>Прямий предок (батько, дід, прадід…).</summary>
    DirectAncestor,

    /// <summary>Прямий нащадок (син, онук, правнук…).</summary>
    DirectDescendant,

    /// <summary>Бічна лінія (брат, дядько, племінник, двоюрідні…).</summary>
    Collateral,

    /// <summary>Подружжя (кровного зв'язку немає).</summary>
    Spouse,

    /// <summary>Зв'язок не встановлено.</summary>
    None,
}

/// <summary>Уточнення для сиблінгів (StepsUp == StepsDown == 1).</summary>
public enum SiblingKind
{
    NotSibling,

    /// <summary>Спільні обидва батьки.</summary>
    Full,

    /// <summary>Спільний лише батько.</summary>
    HalfPaternal,

    /// <summary>Спільна лише мати.</summary>
    HalfMaternal,

    /// <summary>Спільний один із батьків невідомої статі.</summary>
    HalfUnknown,
}
