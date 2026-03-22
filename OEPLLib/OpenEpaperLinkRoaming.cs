namespace OEPLLib;

public sealed class OpenEpaperLinkAccessPointRegistration
{
    public OpenEpaperLinkAccessPointRegistration(string id, Uri baseAddress)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
        BaseAddress = NormalizeBaseAddress(baseAddress);
    }

    public OpenEpaperLinkAccessPointRegistration(string id, string baseAddress)
        : this(id, new Uri(baseAddress, UriKind.Absolute))
    {
    }

    public string Id { get; }

    public Uri BaseAddress { get; }

    public string? Alias { get; set; }

    private static Uri NormalizeBaseAddress(Uri baseAddress)
    {
        var normalized = baseAddress.ToString().TrimEnd('/') + "/";
        return new Uri(normalized, UriKind.Absolute);
    }
}

public sealed class OpenEpaperLinkTagRoamingState
{
    public string Mac { get; init; } = string.Empty;

    public string? Alias { get; set; }

    public string? CurrentAccessPointId { get; set; }

    public bool IsReachable { get; set; }

    public DateTimeOffset? LastSeenUtc { get; set; }

    public DateTimeOffset? LastValidatedUtc { get; set; }

    public DateTimeOffset? LastSearchUtc { get; set; }
}

public sealed class OpenEpaperLinkAccessPointState
{
    public required OpenEpaperLinkAccessPointRegistration Registration { get; init; }

    public string? Alias { get; set; }

    public DateTimeOffset? LastInventoryRefreshUtc { get; set; }

    public DateTimeOffset? LastInventoryAttemptUtc { get; set; }

    public IReadOnlyDictionary<string, OpenEpaperLinkTag> TagsByMac { get; set; } =
        new Dictionary<string, OpenEpaperLinkTag>(StringComparer.OrdinalIgnoreCase);
}

public sealed record OpenEpaperLinkRoamingOptions(
    TimeSpan? InventoryRefreshInterval = null,
    TimeSpan? SearchCooldown = null,
    TimeSpan? RouteValidationInterval = null)
{
    public TimeSpan EffectiveInventoryRefreshInterval => InventoryRefreshInterval ?? TimeSpan.FromSeconds(30);

    public TimeSpan EffectiveSearchCooldown => SearchCooldown ?? TimeSpan.FromSeconds(15);

    public TimeSpan EffectiveRouteValidationInterval => RouteValidationInterval ?? TimeSpan.FromSeconds(20);
}

public sealed record OpenEpaperLinkTagLocation(
    string Mac,
    string? Alias,
    string? AccessPointId,
    Uri? AccessPointBaseAddress,
    bool IsReachable,
    DateTimeOffset? LastSeenUtc,
    DateTimeOffset? LastValidatedUtc,
    DateTimeOffset? LastSearchUtc);

public sealed record OpenEpaperLinkRoamingStateSnapshot(
    IReadOnlyList<OpenEpaperLinkAccessPointSnapshot> AccessPoints,
    IReadOnlyList<OpenEpaperLinkTagRoamingSnapshot> Tags);

public sealed record OpenEpaperLinkAccessPointSnapshot(
    string Id,
    Uri BaseAddress,
    string? Alias,
    DateTimeOffset? LastInventoryAttemptUtc,
    DateTimeOffset? LastInventoryRefreshUtc,
    int KnownTagCount);

public sealed record OpenEpaperLinkTagRoamingSnapshot(
    string Mac,
    string? Alias,
    string? CurrentAccessPointId,
    Uri? CurrentAccessPointBaseAddress,
    bool IsReachable,
    DateTimeOffset? LastSeenUtc,
    DateTimeOffset? LastValidatedUtc,
    DateTimeOffset? LastSearchUtc);

public sealed class OpenEpaperLinkRoamingClient : IDisposable
{
    private readonly Dictionary<string, AccessPointRuntime> _accessPoints;
    private readonly Dictionary<string, OpenEpaperLinkTagRoamingState> _tagsByMac;
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private readonly OpenEpaperLinkRoamingOptions _options;
    private bool _disposed;
    private Action<string>? _debugLog;

    public OpenEpaperLinkRoamingClient(
        IEnumerable<OpenEpaperLinkAccessPointRegistration> accessPoints,
        OpenEpaperLinkRoamingOptions? options = null,
        Func<Uri, OpenEpaperLinkClient>? clientFactory = null)
    {
        ArgumentNullException.ThrowIfNull(accessPoints);

        _options = options ?? new OpenEpaperLinkRoamingOptions();
        var effectiveClientFactory = clientFactory ?? (baseAddress => new OpenEpaperLinkClient(baseAddress));
        _accessPoints = accessPoints
            .Select(registration => new AccessPointRuntime(registration, effectiveClientFactory(registration.BaseAddress)))
            .ToDictionary(item => item.State.Registration.Id, StringComparer.OrdinalIgnoreCase);
        _tagsByMac = new Dictionary<string, OpenEpaperLinkTagRoamingState>(StringComparer.OrdinalIgnoreCase);

        if (_accessPoints.Count == 0)
        {
            throw new ArgumentException("At least one access point must be registered.", nameof(accessPoints));
        }
    }

    public Action<string>? DebugLog
    {
        get => _debugLog;
        set
        {
            _debugLog = value;
            foreach (var accessPoint in _accessPoints.Values)
            {
                accessPoint.Client.DebugLog = value;
            }
        }
    }

    public IReadOnlyCollection<OpenEpaperLinkAccessPointState> AccessPoints =>
        _accessPoints.Values.Select(runtime => runtime.State).ToArray();

    public async Task<OpenEpaperLinkRoamingStateSnapshot> GetStateSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var accessPoints = _accessPoints.Values
                .Select(runtime => new OpenEpaperLinkAccessPointSnapshot(
                    runtime.State.Registration.Id,
                    runtime.State.Registration.BaseAddress,
                    runtime.State.Alias,
                    runtime.State.LastInventoryAttemptUtc,
                    runtime.State.LastInventoryRefreshUtc,
                    runtime.State.TagsByMac.Count))
                .OrderBy(accessPoint => accessPoint.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var tags = _tagsByMac.Values
                .Select(state =>
                {
                    Uri? baseAddress = null;
                    if (state.CurrentAccessPointId is not null &&
                        _accessPoints.TryGetValue(state.CurrentAccessPointId, out var accessPoint))
                    {
                        baseAddress = accessPoint.State.Registration.BaseAddress;
                    }

                    return new OpenEpaperLinkTagRoamingSnapshot(
                        state.Mac,
                        state.Alias,
                        state.CurrentAccessPointId,
                        baseAddress,
                        state.IsReachable,
                        state.LastSeenUtc,
                        state.LastValidatedUtc,
                        state.LastSearchUtc);
                })
                .OrderBy(tag => tag.Alias ?? tag.Mac, StringComparer.OrdinalIgnoreCase)
                .ThenBy(tag => tag.Mac, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new OpenEpaperLinkRoamingStateSnapshot(accessPoints, tags);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<string> FormatStateAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await GetStateSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var lines = new List<string>
        {
            "Access points:"
        };

        foreach (var accessPoint in snapshot.AccessPoints)
        {
            lines.Add(
                $"- {accessPoint.Id} ({accessPoint.BaseAddress}) alias={accessPoint.Alias ?? "<none>"}, tags={accessPoint.KnownTagCount}, lastAttempt={FormatTimestamp(accessPoint.LastInventoryAttemptUtc)}, lastRefresh={FormatTimestamp(accessPoint.LastInventoryRefreshUtc)}");
        }

        lines.Add("Tags:");
        foreach (var tag in snapshot.Tags)
        {
            lines.Add(
                $"- {tag.Alias ?? "<no-alias>"} [{tag.Mac}] ap={tag.CurrentAccessPointId ?? "<none>"} ({tag.CurrentAccessPointBaseAddress?.ToString() ?? "<none>"}), reachable={tag.IsReachable}, lastSeen={FormatTimestamp(tag.LastSeenUtc)}, lastValidated={FormatTimestamp(tag.LastValidatedUtc)}, lastSearch={FormatTimestamp(tag.LastSearchUtc)}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    public async Task<IReadOnlyList<OpenEpaperLinkTag>> GetAllTagsAsync(CancellationToken cancellationToken = default)
    {
        foreach (var accessPoint in _accessPoints.Values)
        {
            await RefreshAccessPointInventoryAsync(accessPoint, forceRefresh: false, cancellationToken).ConfigureAwait(false);
        }

        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _tagsByMac.Values
                .Select(state => CreateLocationSnapshot(state))
                .Where(location => location.IsReachable && location.AccessPointId is not null)
                .Select(location =>
                {
                    var runtime = _accessPoints[location.AccessPointId!];
                    return runtime.State.TagsByMac[location.Mac];
                })
                .ToArray();
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<OpenEpaperLinkTag?> GetTagByMacAsync(string mac, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mac);
        var location = await FindTagLocationByMacAsync(mac, cancellationToken).ConfigureAwait(false);
        if (!location.IsReachable || location.AccessPointId is null)
        {
            return null;
        }

        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _accessPoints[location.AccessPointId].State.TagsByMac.GetValueOrDefault(mac);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<OpenEpaperLinkTag?> GetTagByAliasAsync(string alias, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);

        var cachedMac = await TryFindMacByAliasAsync(alias, cancellationToken).ConfigureAwait(false);
        if (cachedMac is not null)
        {
            return await GetTagByMacAsync(cachedMac, cancellationToken).ConfigureAwait(false);
        }

        foreach (var accessPoint in _accessPoints.Values)
        {
            await RefreshAccessPointInventoryAsync(accessPoint, forceRefresh: false, cancellationToken).ConfigureAwait(false);
        }

        cachedMac = await TryFindMacByAliasAsync(alias, cancellationToken).ConfigureAwait(false);
        return cachedMac is null
            ? null
            : await GetTagByMacAsync(cachedMac, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OpenEpaperLinkTagLocation> FindTagLocationByMacAsync(string mac, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mac);

        var cached = await GetOrCreateTagStateAsync(mac, alias: null, cancellationToken).ConfigureAwait(false);
        if (ShouldUseCurrentRoute(cached))
        {
            return CreateLocationSnapshot(cached);
        }

        var resolved = await ResolveTagLocationAsync(mac, preferredAccessPointId: cached.CurrentAccessPointId, cancellationToken).ConfigureAwait(false);
        return CreateLocationSnapshot(resolved);
    }

    public async Task<OpenEpaperLinkTagLocation?> FindTagLocationByAliasAsync(string alias, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);

        var cachedMac = await TryFindMacByAliasAsync(alias, cancellationToken).ConfigureAwait(false);
        if (cachedMac is not null)
        {
            return await FindTagLocationByMacAsync(cachedMac, cancellationToken).ConfigureAwait(false);
        }

        foreach (var accessPoint in _accessPoints.Values)
        {
            await RefreshAccessPointInventoryAsync(accessPoint, forceRefresh: false, cancellationToken).ConfigureAwait(false);
        }

        cachedMac = await TryFindMacByAliasAsync(alias, cancellationToken).ConfigureAwait(false);
        return cachedMac is null
            ? null
            : await FindTagLocationByMacAsync(cachedMac, cancellationToken).ConfigureAwait(false);
    }

    public async Task UploadJpegAsync(string mac, byte[] fileBytes, OpenEpaperLinkImageUploadOptions? options = null, CancellationToken cancellationToken = default) =>
        await ExecuteForTagAsync(mac, client => client.UploadJpegAsync(mac, fileBytes, options, cancellationToken), cancellationToken).ConfigureAwait(false);

    public async Task UploadRenderedImageAsync(string mac, OeplCanvas canvas, OpenEpaperLinkImageUploadOptions? options = null, CancellationToken cancellationToken = default) =>
        await ExecuteForTagAsync(mac, client => client.UploadRenderedImageAsync(mac, canvas, options, cancellationToken), cancellationToken).ConfigureAwait(false);

    public async Task UploadJsonTemplateAsync(string mac, JsonTemplateDocument document, CancellationToken cancellationToken = default) =>
        await ExecuteForTagAsync(mac, client => client.UploadJsonTemplateAsync(mac, document, cancellationToken), cancellationToken).ConfigureAwait(false);

    public async Task UploadJsonTemplateAsync(string mac, string json, CancellationToken cancellationToken = default) =>
        await ExecuteForTagAsync(mac, client => client.UploadJsonTemplateAsync(mac, json, cancellationToken), cancellationToken).ConfigureAwait(false);

    public async Task SaveTagConfigurationAsync(OpenEpaperLinkTagConfiguration configuration, CancellationToken cancellationToken = default) =>
        await ExecuteForTagAsync(configuration.Mac, client => client.SaveTagConfigurationAsync(configuration, cancellationToken), cancellationToken).ConfigureAwait(false);

    public async Task<OpenEpaperLinkTagType?> GetTagTypeAsync(int hardwareType, CancellationToken cancellationToken = default)
    {
        foreach (var accessPoint in _accessPoints.Values)
        {
            try
            {
                return await accessPoint.Client.GetTagTypeAsync(hardwareType, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Try the next AP before surfacing a failure.
            }
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var accessPoint in _accessPoints.Values)
        {
            accessPoint.Dispose();
        }

        _mutex.Dispose();
        _disposed = true;
    }

    private async Task ExecuteForTagAsync(string mac, Func<OpenEpaperLinkClient, Task> action, CancellationToken cancellationToken)
    {
        var location = await FindTagLocationByMacAsync(mac, cancellationToken).ConfigureAwait(false);
        if (!location.IsReachable || location.AccessPointId is null)
        {
            Debug($"Roaming route lookup for tag '{mac}' found no reachable AP.");
            throw new InvalidOperationException($"Tag '{mac}' is currently unreachable from all registered access points.");
        }

        var initialAccessPointId = location.AccessPointId;
        Debug($"Routing tag '{mac}' to AP '{initialAccessPointId}' ({location.AccessPointBaseAddress}).");
        try
        {
            await action(_accessPoints[initialAccessPointId].Client).ConfigureAwait(false);
            await MarkRouteValidatedAsync(mac, initialAccessPointId, cancellationToken).ConfigureAwait(false);
            Debug($"Tag '{mac}' update succeeded through AP '{initialAccessPointId}'.");
        }
        catch (Exception ex)
        {
            Debug($"Tag '{mac}' update through AP '{initialAccessPointId}' failed with {ex.GetType().Name}: {ex.Message}. Attempting relocation.");
            var relocated = await ResolveTagLocationAsync(mac, preferredAccessPointId: initialAccessPointId, cancellationToken).ConfigureAwait(false);
            if (!relocated.IsReachable || relocated.CurrentAccessPointId is null)
            {
                Debug($"Relocation for tag '{mac}' failed. No AP currently reports the tag.");
                throw new InvalidOperationException($"Tag '{mac}' is currently unreachable from all registered access points.");
            }

            if (string.Equals(relocated.CurrentAccessPointId, initialAccessPointId, StringComparison.OrdinalIgnoreCase))
            {
                Debug($"Relocation for tag '{mac}' stayed on AP '{initialAccessPointId}'. Rethrowing original failure.");
                throw;
            }

            Debug($"Retrying tag '{mac}' update through relocated AP '{relocated.CurrentAccessPointId}'.");
            await action(_accessPoints[relocated.CurrentAccessPointId].Client).ConfigureAwait(false);
            await MarkRouteValidatedAsync(mac, relocated.CurrentAccessPointId, cancellationToken).ConfigureAwait(false);
            Debug($"Tag '{mac}' update succeeded after relocation to AP '{relocated.CurrentAccessPointId}'.");
        }
    }

    private async Task<OpenEpaperLinkTagRoamingState> ResolveTagLocationAsync(string mac, string? preferredAccessPointId, CancellationToken cancellationToken)
    {
        Debug($"Resolving roaming location for tag '{mac}'. Preferred AP='{preferredAccessPointId ?? "<none>"}'.");
        if (preferredAccessPointId is not null && _accessPoints.TryGetValue(preferredAccessPointId, out var preferred))
        {
            var found = await TryRefreshAndLocateOnAccessPointAsync(preferred, mac, forceRefresh: true, cancellationToken).ConfigureAwait(false);
            if (found is not null)
            {
                Debug($"Tag '{mac}' confirmed on preferred AP '{preferredAccessPointId}'.");
                return found;
            }
        }

        var canSearch = await CanSearchNowAsync(mac, cancellationToken).ConfigureAwait(false);
        if (!canSearch)
        {
            Debug($"Skipping AP search for tag '{mac}' because search cooldown is still active.");
            return await GetOrCreateTagStateAsync(mac, alias: null, cancellationToken).ConfigureAwait(false);
        }

        foreach (var accessPoint in _accessPoints.Values.Where(item => !string.Equals(item.State.Registration.Id, preferredAccessPointId, StringComparison.OrdinalIgnoreCase)))
        {
            var found = await TryRefreshAndLocateOnAccessPointAsync(accessPoint, mac, forceRefresh: false, cancellationToken).ConfigureAwait(false);
            if (found is not null)
            {
                Debug($"Tag '{mac}' relocated to AP '{accessPoint.State.Registration.Id}'.");
                return found;
            }
        }

        Debug($"No AP reported tag '{mac}'. Marking it unreachable.");
        return await MarkTagUnreachableAsync(mac, cancellationToken).ConfigureAwait(false);
    }

    private async Task<OpenEpaperLinkTagRoamingState?> TryRefreshAndLocateOnAccessPointAsync(
        AccessPointRuntime accessPoint,
        string mac,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        Debug($"Checking AP '{accessPoint.State.Registration.Id}' for tag '{mac}' (forceRefresh={forceRefresh}).");
        await RefreshAccessPointInventoryAsync(accessPoint, forceRefresh, cancellationToken).ConfigureAwait(false);

        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!accessPoint.State.TagsByMac.TryGetValue(mac, out var tag))
            {
                Debug($"AP '{accessPoint.State.Registration.Id}' does not currently list tag '{mac}'.");
                return null;
            }

            var state = GetOrCreateTagStateUnsafe(mac, tag.Alias);
            state.Alias = tag.Alias ?? state.Alias;
            state.CurrentAccessPointId = accessPoint.State.Registration.Id;
            state.IsReachable = true;
            state.LastSeenUtc = DateTimeOffset.UtcNow;
            state.LastValidatedUtc = DateTimeOffset.UtcNow;
            Debug($"AP '{accessPoint.State.Registration.Id}' reports tag '{mac}' with alias '{state.Alias ?? "<none>"}'.");
            return state;
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task RefreshAccessPointInventoryAsync(AccessPointRuntime accessPoint, bool forceRefresh, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var accessPointId = accessPoint.State.Registration.Id;

        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var lastRefreshUtc = accessPoint.State.LastInventoryRefreshUtc;
            if (!forceRefresh &&
                lastRefreshUtc is not null &&
                now - lastRefreshUtc.Value < _options.EffectiveInventoryRefreshInterval)
            {
                Debug($"Skipping inventory refresh for AP '{accessPointId}' because cached inventory is still fresh.");
                return;
            }

            accessPoint.State.LastInventoryAttemptUtc = now;
        }
        finally
        {
            _mutex.Release();
        }

        Debug($"Refreshing inventory from AP '{accessPointId}' at {accessPoint.State.Registration.BaseAddress}.");
        var tags = await accessPoint.Client.GetAllTagsAsync(cancellationToken).ConfigureAwait(false);
        var tagsByMac = tags.ToDictionary(tag => tag.Mac, StringComparer.OrdinalIgnoreCase);
        var refreshTime = DateTimeOffset.UtcNow;
        Debug($"AP '{accessPointId}' inventory refresh completed with {tagsByMac.Count} tag(s).");

        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            accessPoint.State.TagsByMac = tagsByMac;
            accessPoint.State.LastInventoryRefreshUtc = refreshTime;

            foreach (var tag in tags)
            {
                var state = GetOrCreateTagStateUnsafe(tag.Mac, tag.Alias);
                state.Alias = tag.Alias ?? state.Alias;
                state.CurrentAccessPointId = accessPoint.State.Registration.Id;
                state.IsReachable = true;
                state.LastSeenUtc = refreshTime;
                state.LastValidatedUtc = refreshTime;
            }

            var missingOnThisAccessPoint = _tagsByMac.Values
                .Where(tag => string.Equals(tag.CurrentAccessPointId, accessPoint.State.Registration.Id, StringComparison.OrdinalIgnoreCase))
                .Where(tag => !tagsByMac.ContainsKey(tag.Mac))
                .ToArray();

            foreach (var tag in missingOnThisAccessPoint)
            {
                tag.CurrentAccessPointId = null;
                tag.IsReachable = false;
                tag.LastValidatedUtc = refreshTime;
                Debug($"Tag '{tag.Mac}' is no longer listed on AP '{accessPointId}'. Cleared current route.");
            }
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task<OpenEpaperLinkTagRoamingState> GetOrCreateTagStateAsync(string mac, string? alias, CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return GetOrCreateTagStateUnsafe(mac, alias);
        }
        finally
        {
            _mutex.Release();
        }
    }

    private OpenEpaperLinkTagRoamingState GetOrCreateTagStateUnsafe(string mac, string? alias)
    {
        if (!_tagsByMac.TryGetValue(mac, out var state))
        {
            state = new OpenEpaperLinkTagRoamingState
            {
                Mac = mac,
                Alias = alias,
                IsReachable = false
            };
            _tagsByMac.Add(mac, state);
        }
        else if (!string.IsNullOrWhiteSpace(alias))
        {
            state.Alias = alias;
        }

        return state;
    }

    private async Task<string?> TryFindMacByAliasAsync(string alias, CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _tagsByMac.Values
                .FirstOrDefault(state => string.Equals(state.Alias, alias, StringComparison.OrdinalIgnoreCase))
                ?.Mac;
        }
        finally
        {
            _mutex.Release();
        }
    }

    private bool ShouldUseCurrentRoute(OpenEpaperLinkTagRoamingState state)
    {
        if (!state.IsReachable || state.CurrentAccessPointId is null || state.LastValidatedUtc is null)
        {
            return false;
        }

        return DateTimeOffset.UtcNow - state.LastValidatedUtc.Value < _options.EffectiveRouteValidationInterval;
    }

    private async Task<bool> CanSearchNowAsync(string mac, CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = GetOrCreateTagStateUnsafe(mac, alias: null);
            var now = DateTimeOffset.UtcNow;
            if (state.LastSearchUtc is not null &&
                now - state.LastSearchUtc.Value < _options.EffectiveSearchCooldown)
            {
                return false;
            }

            state.LastSearchUtc = now;
            Debug($"Starting AP search for tag '{mac}'.");
            return true;
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task<OpenEpaperLinkTagRoamingState> MarkTagUnreachableAsync(string mac, CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = GetOrCreateTagStateUnsafe(mac, alias: null);
            state.CurrentAccessPointId = null;
            state.IsReachable = false;
            state.LastValidatedUtc = DateTimeOffset.UtcNow;
            Debug($"Tag '{mac}' marked unreachable.");
            return state;
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task MarkRouteValidatedAsync(string mac, string accessPointId, CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = GetOrCreateTagStateUnsafe(mac, alias: null);
            state.CurrentAccessPointId = accessPointId;
            state.IsReachable = true;
            state.LastValidatedUtc = DateTimeOffset.UtcNow;
            Debug($"Validated tag '{mac}' on AP '{accessPointId}'.");
        }
        finally
        {
            _mutex.Release();
        }
    }

    private OpenEpaperLinkTagLocation CreateLocationSnapshot(OpenEpaperLinkTagRoamingState state)
    {
        Uri? accessPointBaseAddress = null;
        if (state.CurrentAccessPointId is not null &&
            _accessPoints.TryGetValue(state.CurrentAccessPointId, out var accessPoint))
        {
            accessPointBaseAddress = accessPoint.State.Registration.BaseAddress;
        }

        return new OpenEpaperLinkTagLocation(
            state.Mac,
            state.Alias,
            state.CurrentAccessPointId,
            accessPointBaseAddress,
            state.IsReachable,
            state.LastSeenUtc,
            state.LastValidatedUtc,
            state.LastSearchUtc);
    }

    private void Debug(string message) => _debugLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] {message}");

    private static string FormatTimestamp(DateTimeOffset? timestamp) =>
        timestamp?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "<never>";

    private sealed class AccessPointRuntime : IDisposable
    {
        public AccessPointRuntime(OpenEpaperLinkAccessPointRegistration registration, OpenEpaperLinkClient client)
        {
            State = new OpenEpaperLinkAccessPointState
            {
                Registration = registration,
                Alias = registration.Alias
            };
            Client = client;
        }

        public OpenEpaperLinkAccessPointState State { get; }

        public OpenEpaperLinkClient Client { get; }

        public void Dispose() => Client.Dispose();
    }
}
