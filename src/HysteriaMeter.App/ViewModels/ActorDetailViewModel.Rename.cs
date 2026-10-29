using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HysteriaMeter.Combat;

namespace HysteriaMeter.App.ViewModels;

/// <summary>Diálogo para renombrar una skill (doble clic en su fila; mock Avisos, "Renombrar skill").</summary>
public sealed partial class ActorDetailViewModel
{
    /// <summary>Skill que se está renombrando (doble clic en su fila); <c>null</c> si ninguna.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRenamingSkill), nameof(RenamingSkillLabel), nameof(RenameSubtitle), nameof(HasRenameVariants), nameof(RenameVariantsText))]
    public partial SkillRowViewModel? RenamingSkill { get; set; }

    public bool IsRenamingSkill => RenamingSkill is not null;

    public string RenamingSkillLabel => RenamingSkill is { } skill ? $"Nombre para la skill {skill.SkillId}:" : string.Empty;

    /// <summary>"Hechicero · ID 15030340 · base 15030000": la clase, el ID (si ya tiene nombre) y la skill base.</summary>
    public string RenameSubtitle => RenamingSkill is { } skill ? RenameSubtitleOf(skill.SkillId, skill.Name) : string.Empty;

    public static string RenameSubtitleOf(uint skillId, string name) =>
        string.Join(" · ", new[]
        {
            PlayerClasses.Of(EntityRegistry.ClassOfSkill(skillId))?.Name,
            name.StartsWith("Skill ", StringComparison.Ordinal) ? null : $"ID {skillId}",
            SkillNames.BaseOf(skillId) is { } baseId ? $"base {baseId}" : null,
        }.OfType<string>());

    /// <summary>Solo las skills de clase tienen variantes (nivel y variante en los últimos cuatro dígitos).</summary>
    public bool HasRenameVariants => RenamingSkill is { } skill && SkillNames.BaseOf(skill.SkillId) is not null;

    /// <summary>"Aplicar a todas las variantes (1503xxxx)": las que comparten la skill base.</summary>
    public string RenameVariantsText => RenamingSkill is { } skill && SkillNames.BaseOf(skill.SkillId) is { } baseId
        ? $"Aplicar a todas las variantes ({baseId / 10_000}xxxx)"
        : string.Empty;

    /// <summary>
    /// Con "Aplicar a todas las variantes" (por defecto) el nombre va a la skill base y las variantes que tenían uno
    /// propio lo pierden: todas toman el de la base.
    /// </summary>
    [ObservableProperty]
    public partial bool ApplyToVariants { get; set; } = true;

    [ObservableProperty]
    public partial string SkillNameDraft { get; set; } = string.Empty;

    /// <summary>Se guardó un nombre de skill (vacío lo saca); lo persiste quien guarda las preferencias.</summary>
    public event Action<uint, string>? SkillRenamed;

    [RelayCommand]
    private void StartRenameSkill(SkillRowViewModel? skill)
    {
        // La fila "Invocaciones" junta varias skills: no tiene un ID para nombrar.
        if (skill is { IsSummonGroup: true })
        {
            return;
        }

        RenamingSkill = skill;
        ApplyToVariants = true;
        SkillNameDraft = skill is null || skill.Name.StartsWith("Skill ", StringComparison.Ordinal) ? string.Empty : skill.Name;
    }

    [RelayCommand]
    private void SaveSkillName()
    {
        if (RenamingSkill is { } skill)
        {
            if (ApplyToVariants && SkillNames.BaseOf(skill.SkillId) is { } baseId)
            {
                // La lista se toma antes: quien guarda las preferencias reemplaza los nombres en cada aviso.
                var variants = names.Custom.Keys.Where(id => SkillNames.BaseOf(id) == baseId).ToList();
                SkillRenamed?.Invoke(baseId, SkillNameDraft);
                foreach (var variant in variants)
                {
                    SkillRenamed?.Invoke(variant, string.Empty);
                }
            }
            else
            {
                SkillRenamed?.Invoke(skill.SkillId, SkillNameDraft);
            }

            // Cambia el nombre de todas las filas: las otras variantes de la misma skill también.
            skill.Name = names.Of(skill.SkillId);
            foreach (var row in Skills.Where(r => !r.IsSummonGroup))
            {
                row.Name = names.Of(row.SkillId);
            }
        }

        RenamingSkill = null;
    }

    [RelayCommand]
    private void CancelSkillRename() => RenamingSkill = null;
}
