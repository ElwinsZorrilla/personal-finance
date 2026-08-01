using System.Globalization;

namespace Margen.Worker;

/// <summary>
/// Lee `infra/.env` cuando se ejecuta a mano desde el repositorio.
/// </summary>
/// <remarks>
/// En el servidor no hace falta: el compose pasa las variables al contenedor.
/// Existe para la ejecución local, y sobre todo para la captura de muestras,
/// donde la alternativa es escribir la contraseña en la línea de órdenes.
///
/// Escribirla ahí tiene dos problemas y los dos son reales: `bash` expande el
/// historial al ver un `!` y se come parte de la contraseña antes de que el
/// programa la vea, y la línea entera queda guardada en `~/.bash_history` en
/// claro. En un archivo que está en `.gitignore` no pasa ninguna de las dos.
///
/// **Lo que ya está en el entorno gana.** El archivo rellena huecos, no pisa
/// nada: quien exporta una variable a propósito espera que valga.
/// </remarks>
public static class DotEnv
{
    /// <summary>
    /// Los nombres del `.env` —que son los que consume el compose— y su
    /// equivalente en la configuración de .NET.
    /// </summary>
    private static readonly Dictionary<string, string> Mapping = new(StringComparer.Ordinal)
    {
        ["IMAP_HOST"] = "Imap__Host",
        ["IMAP_PORT"] = "Imap__Port",
        ["IMAP_USER"] = "Imap__User",
        ["IMAP_PASSWORD"] = "Imap__Password",
        ["IMAP_ALLOWED_SENDERS"] = "Imap__AllowedSenders",
        ["MUESTRAS_DATOS_PERSONALES"] = "Muestras__DatosPersonales",
    };

    /// <summary>Carga el archivo si existe. Devuelve cuántas variables puso.</summary>
    public static int Load()
    {
        string? path = Find();
        if (path is null) return 0;

        int applied = 0;

        foreach (string line in File.ReadLines(path))
        {
            string trimmed = line.Trim();

            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;

            int equals = trimmed.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0) continue;

            string key = trimmed[..equals].Trim();
            string value = trimmed[(equals + 1)..].Trim();

            // Las comillas son del archivo, no del valor.
            if (value.Length >= 2
                && ((value[0] == '"' && value[^1] == '"')
                    || (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }

            if (value.Length == 0) continue;

            string target = Mapping.TryGetValue(key, out string? mapped) ? mapped : key;

            // Lo que ya está puesto gana.
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(target))) continue;

            Environment.SetEnvironmentVariable(target, value);
            applied++;
        }

        if (applied > 0)
        {
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"Leídas {applied} variable(s) de {path}"));
        }

        return applied;
    }

    private static string? Find()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "infra", ".env");
            if (File.Exists(candidate)) return candidate;

            string root = Path.Combine(directory.FullName, ".env");
            if (File.Exists(root)) return root;

            directory = directory.Parent;
        }

        return null;
    }
}
