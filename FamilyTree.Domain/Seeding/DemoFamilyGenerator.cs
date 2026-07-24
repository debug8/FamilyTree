namespace FamilyTree.Domain.Seeding;

/// <summary>
/// Генератор демо-родини (T-5.5): будує дерево нащадків від подружжя-засновників і додає
/// «складні» зв'язки, щоб продемонструвати різноманіття назв родства й свояцтва:
/// рідні та зведені сиблінги, дядьки/тітки, племінники, двоюрідні/троюрідні (кузени),
/// прабатьки/правнуки, подружжя й колишнє подружжя, невідома стать.
///
/// Повертає доменні сутності (без залежностей від сховища чи UI). Логіка колишнього
/// консольного SeedGenerator винесена сюди й параметризована через <see cref="DemoFamilyOptions"/>.
/// За заданого <see cref="DemoFamilyOptions.Seed"/> результат детермінований.
/// </summary>
public sealed class DemoFamilyGenerator
{
    private const int GenerationGap = 25; // середня різниця поколінь, років

    private static readonly string[] MaleNames =
    {
        "Іван", "Петро", "Микола", "Андрій", "Сергій", "Василь", "Олег", "Тарас",
        "Богдан", "Юрій", "Роман", "Дмитро", "Максим", "Степан", "Григорій",
    };

    private static readonly string[] FemaleNames =
    {
        "Марія", "Ольга", "Ганна", "Наталія", "Оксана", "Тетяна", "Ірина", "Софія",
        "Катерина", "Людмила", "Віра", "Олена", "Дарина", "Зоряна", "Галина",
    };

    private static readonly string[] Surnames =
    {
        "Коваленко", "Шевченко", "Ткаченко", "Бондаренко", "Кравченко", "Мельник",
        "Поліщук", "Савчук", "Іваненко", "Мороз", "Гончар", "Лисенко", "Марченко",
        "Панченко", "Гриценко",
    };

    private readonly DemoFamilyOptions _opts;
    private readonly Random _rnd;
    private readonly int _currentYear;

    private readonly List<Person> _persons = new();
    private readonly List<ParentChildLink> _links = new();
    private readonly List<SpouseLink> _spouses = new();
    private readonly Dictionary<Guid, int> _generationOf = new();

    private DemoFamilyGenerator(DemoFamilyOptions options)
    {
        _opts = options.Normalized();
        _rnd = _opts.Seed is { } seed ? new Random(seed) : new Random();
        _currentYear = DateTime.Today.Year;
    }

    /// <summary>Створює демо-родину за параметрами (значення поза межами обрізаються).</summary>
    public static DemoFamilyResult Generate(DemoFamilyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new DemoFamilyGenerator(options).Build();
    }

    private DemoFamilyResult Build()
    {
        var youngestBirthYear = _currentYear - 16;
        var baseYear = youngestBirthYear - (_opts.Generations - 1) * GenerationGap;

        // Подружжя-засновники (покоління 0).
        var founderSurname = Surname();
        var husband = AddPerson(founderSurname, Gender.Male, baseYear, patronymicFrom: null, maiden: null, generation: 0);
        var wife = AddPerson(founderSurname, Gender.Female, baseYear + _rnd.Next(-2, 3), patronymicFrom: null, maiden: Surname(), generation: 0);
        Marry(husband.Id, wife.Id, baseYear + _rnd.Next(22, 28), allowDivorce: false);

        var queue = new Queue<Couple>();
        queue.Enqueue(new Couple(husband.Id, husband.FirstName, wife.Id, founderSurname, 0, baseYear));

        while (queue.Count > 0 && _persons.Count < _opts.MaxPersons)
        {
            var couple = queue.Dequeue();
            if (couple.Generation >= _opts.Generations - 1)
            {
                continue; // останнє покоління дітей не має
            }

            var childGeneration = couple.Generation + 1;
            var childrenCount = _rnd.Next(2, _opts.MaxChildrenPerCouple + 1);

            for (var c = 0; c < childrenCount && _persons.Count < _opts.MaxPersons; c++)
            {
                var gender = PickChildGender();
                var childBirth = couple.ParentsBirthYear + _rnd.Next(24, 43); // батькам 24–42
                var child = AddPerson(couple.Surname, gender, childBirth, couple.FatherFirst, maiden: null, childGeneration);
                _links.Add(Parent(couple.FatherId, child.Id));
                _links.Add(Parent(couple.MotherId, child.Id));

                // Одружуємо тих, хто не в останньому поколінні (щоб мали власних дітей → кузени).
                if (childGeneration < _opts.Generations - 1 && gender != Gender.Unknown && _persons.Count < _opts.MaxPersons)
                {
                    var marryYear = childBirth + _rnd.Next(20, 30);
                    if (gender == Gender.Female)
                    {
                        var hSurname = Surname();
                        var spouse = AddPerson(hSurname, Gender.Male, childBirth + _rnd.Next(-3, 4), patronymicFrom: null, maiden: null, childGeneration);
                        Marry(child.Id, spouse.Id, marryYear, allowDivorce: true);
                        queue.Enqueue(new Couple(spouse.Id, spouse.FirstName, child.Id, hSurname, childGeneration, childBirth));
                    }
                    else
                    {
                        var spouse = AddPerson(couple.Surname, Gender.Female, childBirth + _rnd.Next(-3, 4), patronymicFrom: null, maiden: Surname(), childGeneration);
                        Marry(child.Id, spouse.Id, marryYear, allowDivorce: true);
                        queue.Enqueue(new Couple(child.Id, child.FirstName, spouse.Id, couple.Surname, childGeneration, childBirth));
                    }
                }
            }

            // Складні зв'язки: зведений сиблінг від другого партнера батька (спільний лише батько).
            if (_opts.ComplexRelations && _persons.Count < _opts.MaxPersons && _rnd.NextDouble() < 0.35)
            {
                AddHalfSibling(couple, childGeneration);
            }
        }

        return new DemoFamilyResult(_persons, _links, _spouses, PickRoot());
    }

    /// <summary>
    /// Додає зведеного сиблінга: другий партнер батька та спільна з ним дитина.
    /// Дитина ділить із дітьми пари лише батька → «зведений брат/сестра по батькові».
    /// Другий шлюб часто розірваний (демонстрація колишнього подружжя).
    /// </summary>
    private void AddHalfSibling(Couple couple, int childGeneration)
    {
        var partner = AddPerson(Surname(), Gender.Female, couple.ParentsBirthYear + _rnd.Next(-4, 5), patronymicFrom: null, maiden: Surname(), couple.Generation);
        Marry(couple.FatherId, partner.Id, couple.ParentsBirthYear + _rnd.Next(20, 30), allowDivorce: true);

        if (_persons.Count >= _opts.MaxPersons)
        {
            return;
        }

        var gender = PickChildGender();
        var childBirth = couple.ParentsBirthYear + _rnd.Next(24, 43);
        var halfChild = AddPerson(couple.Surname, gender, childBirth, couple.FatherFirst, maiden: null, childGeneration);
        _links.Add(Parent(couple.FatherId, halfChild.Id));
        _links.Add(Parent(partner.Id, halfChild.Id));
    }

    private Person AddPerson(string surname, Gender gender, int birthYear, string? patronymicFrom, string? maiden, int generation)
    {
        var first = gender switch
        {
            Gender.Female => FemaleNames[_rnd.Next(FemaleNames.Length)],
            Gender.Male => MaleNames[_rnd.Next(MaleNames.Length)],
            _ => _rnd.NextDouble() < 0.5 ? MaleNames[_rnd.Next(MaleNames.Length)] : FemaleNames[_rnd.Next(FemaleNames.Length)],
        };

        var person = new Person
        {
            LastName = surname,
            FirstName = first,
            Gender = gender,
            BirthDate = RandomDate(birthYear),
            MaidenName = maiden,
            MiddleName = patronymicFrom is not null && gender != Gender.Unknown
                ? Patronymic(patronymicFrom, gender == Gender.Female)
                : null,
        };

        // Померлі — ті, хто прожив би вже понад ~85 років (і чия дата смерті — у минулому).
        if (birthYear + 85 < _currentYear)
        {
            var deathYear = birthYear + _rnd.Next(63, 90);
            if (deathYear < _currentYear)
            {
                person.DeathDate = RandomDate(deathYear);
            }
        }

        _persons.Add(person);
        _generationOf[person.Id] = generation;
        return person;
    }

    private void Marry(Guid a, Guid b, int marriageYear, bool allowDivorce)
    {
        DateOnly? divorce = null;
        if (allowDivorce && _opts.IncludeDivorces && _rnd.NextDouble() < 0.25)
        {
            var divorceYear = marriageYear + _rnd.Next(3, 25);
            if (divorceYear < _currentYear)
            {
                divorce = RandomDate(divorceYear);
            }
        }

        _spouses.Add(SpouseLink.Create(a, b, RandomDate(marriageYear), divorce));
    }

    private Gender PickChildGender()
    {
        if (_opts.ComplexRelations && _rnd.NextDouble() < 0.08)
        {
            return Gender.Unknown;
        }

        return _rnd.NextDouble() < 0.5 ? Gender.Female : Gender.Male;
    }

    private DateOnly RandomDate(int year) => new(year, _rnd.Next(1, 13), _rnd.Next(1, 28));

    private string Surname() => Surnames[_rnd.Next(Surnames.Length)];

    private static ParentChildLink Parent(Guid parentId, Guid childId) =>
        new() { ParentId = parentId, ChildId = childId, ParentRole = ParentRole.Biological };

    private static string Patronymic(string fatherFirst, bool female)
    {
        var stem = fatherFirst;
        if (stem.Length > 0 && stem[^1] is 'о' or 'ь' or 'а' or 'я' or 'й')
        {
            stem = stem[..^1];
        }

        return female ? stem + "івна" : stem + "ович";
    }

    /// <summary>
    /// Обирає кореневу особу з найбагатшим оточенням: має і предків, і нащадків,
    /// ближче до середини дерева й з більшою кількістю сиблінгів.
    /// </summary>
    private Guid? PickRoot()
    {
        if (_persons.Count == 0)
        {
            return null;
        }

        var parentIds = _links.Select(l => l.ParentId).ToHashSet();
        var childIds = _links.Select(l => l.ChildId).ToHashSet();

        var candidates = _persons.Where(p => parentIds.Contains(p.Id) && childIds.Contains(p.Id)).ToList();
        if (candidates.Count == 0)
        {
            candidates = _persons.Where(p => childIds.Contains(p.Id)).ToList();
        }

        if (candidates.Count == 0)
        {
            return _persons[0].Id;
        }

        var target = _opts.Generations / 2;
        return candidates
            .OrderBy(p => Math.Abs(_generationOf[p.Id] - target))
            .ThenByDescending(p => SiblingCount(p.Id))
            .First().Id;
    }

    private int SiblingCount(Guid personId)
    {
        var parents = _links.Where(l => l.ChildId == personId).Select(l => l.ParentId).ToHashSet();
        return _links
            .Where(l => parents.Contains(l.ParentId) && l.ChildId != personId)
            .Select(l => l.ChildId)
            .Distinct()
            .Count();
    }

    private readonly record struct Couple(
        Guid FatherId, string FatherFirst, Guid MotherId, string Surname, int Generation, int ParentsBirthYear);
}
