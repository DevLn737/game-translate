namespace ScreenTranslate;

internal static class SettingsSavePolicy
{
    internal static string? Save(Preferences next, Preferences previous, Func<Preferences, string?> apply, Func<Preferences, bool> persist)
    {
        if (!next.IsValid) return "Укажите разные допустимые клавиши и время показа от 3 до 20 секунд.";
        var error = apply(next);
        if (error is not null) return error;
        if (persist(next)) return null;
        var rollback = apply(previous);
        return rollback is null ? "Не удалось сохранить настройки." : "Не удалось сохранить настройки. Не удалось восстановить горячие клавиши: " + rollback;
    }
}
