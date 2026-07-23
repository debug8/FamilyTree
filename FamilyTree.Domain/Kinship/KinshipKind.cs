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

    /// <summary>Свояцтво — зв'язок через шлюб, без кровної спорідненості (розд. 4.5).</summary>
    Affinity,

    /// <summary>Зв'язок не встановлено.</summary>
    None,
}

/// <summary>
/// Різновид свояцтва (зв'язок через шлюб, розд. 4.5). Схема:
/// A —(кров)— X —(шлюб)— B  (патерн A) або  A —(шлюб)— X —(кров)— B  (патерн B).
/// Конкретну назву обирає форматер за цим різновидом, статтю особи-B та статтю сполучної особи X.
/// </summary>
public enum AffinityKind
{
    NotAffinity,

    /// <summary>B — батько/мати мого подружжя (тесть/теща або свекор/свекруха).</summary>
    SpouseParent,

    /// <summary>B — брат/сестра мого подружжя (дівер/зовиця або шурин/своячка).</summary>
    SpouseSibling,

    /// <summary>B — подружжя моєї дитини (зять/невістка).</summary>
    ChildSpouse,

    /// <summary>B — подружжя мого брата/сестри (зять-шваґер / невістка-братова).</summary>
    SiblingSpouse,

    /// <summary>B — подружжя мого дядька/тітки (описово: «чоловік тітки», «дружина дядька»).</summary>
    UncleAuntSpouse,
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
