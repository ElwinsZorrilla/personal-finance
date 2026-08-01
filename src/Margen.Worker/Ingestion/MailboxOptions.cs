namespace Margen.Worker.Ingestion;

/// <summary>Ajustes del buzón. Llegan por variables de entorno.</summary>
public sealed class MailboxOptions
{
    public const string SectionName = "Imap";

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 993;

    public string User { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Remitentes admitidos, separados por comas.
    /// </summary>
    /// <remarks>
    /// Vacía por defecto y a propósito: sin lista configurada **no se lee
    /// nada**. La alternativa —leerlo todo— convertiría el buzón en una entrada
    /// abierta al sistema: cualquiera que sepa la dirección podría mandar un
    /// correo con formato de banco y crear movimientos.
    /// </remarks>
    public string AllowedSenders { get; set; } = string.Empty;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// La lista, ya partida y normalizada. Se compara por dominio o por
    /// dirección completa: `alertas@banco.do` y `banco.do` valen los dos.
    /// </summary>
    public IReadOnlyList<string> Senders =>
    [
        .. AllowedSenders
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToLowerInvariant()),
    ];

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host)
        && !string.IsNullOrWhiteSpace(User)
        && Senders.Count > 0;

    /// <summary>Si el remitente está en la lista blanca.</summary>
    public bool Allows(string sender)
    {
        if (string.IsNullOrWhiteSpace(sender)) return false;

        string normalized = sender.Trim().ToLowerInvariant();

        foreach (string allowed in Senders)
        {
            if (normalized == allowed) return true;

            // Coincidencia por dominio, anclada al `@`. Sin el ancla,
            // `banco.do` dejaría entrar `banco.do.atacante.com`.
            if (normalized.EndsWith('@' + allowed, StringComparison.Ordinal)) return true;
        }

        return false;
    }
}
