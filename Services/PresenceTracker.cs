namespace gestao_chamados.Services;

public sealed record PresenceViewer(string UserId, string Name);

/// <summary>
/// Quem está com cada chamado aberto neste momento (em memória, por conexão SignalR).
/// Um mesmo usuário com duas abas aparece uma só vez. Lógica pura, sem SignalR, para ser testável.
/// </summary>
public sealed class PresenceTracker
{
    private readonly object _gate = new();
    private readonly Dictionary<int, Dictionary<string, PresenceViewer>> _tickets = [];

    /// <summary>Registra a conexão no chamado e devolve quem está vendo agora.</summary>
    public IReadOnlyList<PresenceViewer> Join(int ticketId, string connectionId, PresenceViewer viewer)
    {
        lock (_gate)
        {
            if (!_tickets.TryGetValue(ticketId, out var connections))
            {
                _tickets[ticketId] = connections = [];
            }

            connections[connectionId] = viewer;
            return Snapshot(connections);
        }
    }

    /// <summary>Remove a conexão do chamado e devolve quem continua vendo.</summary>
    public IReadOnlyList<PresenceViewer> Leave(int ticketId, string connectionId)
    {
        lock (_gate)
        {
            if (!_tickets.TryGetValue(ticketId, out var connections))
            {
                return [];
            }

            connections.Remove(connectionId);
            if (connections.Count == 0)
            {
                _tickets.Remove(ticketId);
                return [];
            }

            return Snapshot(connections);
        }
    }

    /// <summary>Remove a conexão de todos os chamados (queda ou fechamento da aba).</summary>
    public IReadOnlyList<(int TicketId, IReadOnlyList<PresenceViewer> Viewers)> LeaveAll(string connectionId)
    {
        lock (_gate)
        {
            var affected = new List<(int, IReadOnlyList<PresenceViewer>)>();

            foreach (var (ticketId, connections) in _tickets.ToList())
            {
                if (!connections.Remove(connectionId))
                {
                    continue;
                }

                if (connections.Count == 0)
                {
                    _tickets.Remove(ticketId);
                    affected.Add((ticketId, []));
                }
                else
                {
                    affected.Add((ticketId, Snapshot(connections)));
                }
            }

            return affected;
        }
    }

    public IReadOnlyList<PresenceViewer> Viewers(int ticketId)
    {
        lock (_gate)
        {
            return _tickets.TryGetValue(ticketId, out var connections) ? Snapshot(connections) : [];
        }
    }

    public bool TryGetViewer(int ticketId, string connectionId, out PresenceViewer viewer)
    {
        lock (_gate)
        {
            if (_tickets.TryGetValue(ticketId, out var connections) && connections.TryGetValue(connectionId, out var found))
            {
                viewer = found;
                return true;
            }

            viewer = null!;
            return false;
        }
    }

    private static List<PresenceViewer> Snapshot(Dictionary<string, PresenceViewer> connections) =>
        connections.Values
            .DistinctBy(v => v.UserId)
            .OrderBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
}
