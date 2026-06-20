// src/Ets.Infrastructure/ExternalClients/TeamPlus/TeamPlusSystemClient_VirtualAccount.cs
// 此為 TeamPlusSystemClient 的 partial class 補充，
// 需將以下兩個 method 加入 TeamPlusSystemClient.cs（可直接 append 至類別結尾 } 前）
//
// ─── 需要加入的 using（若 TeamPlusSystemClient.cs 頂端尚未有）───
// using Ets.Application.Dtos.TeamPlus;
//
// ─── 需要加入的常數（類別內）───
// private const string TeamServicePath = "/API/TeamService.ashx";

/*
    // ─────────────────────────────────────────────────────────────
    // §6.6.3 建立團隊虛擬帳號
    // ─────────────────────────────────────────────────────────────
    public async Task<CreateTeamApiAccountResult> CreateTeamApiAccountAsync(
        long teamSn,
        string ownerAccount,
        string accountName,
        CancellationToken ct = default)
    {
        var form = BuildBaseForm("createTeamAPIAccount");
        form.Add(KV("team_sn", teamSn.ToString()));
        form.Add(KV("owner",   ownerAccount));
        form.Add(KV("name",    accountName));
        form.Add(KV("icon",    string.Empty));

        var raw = await ExecuteWithPollyAsync(form, ct);
        var dto = Deserialize<CreateTeamApiAccountResponseDto>(raw, "createTeamAPIAccount");

        _logger.LogInformation(
            "createTeamAPIAccount 完成：IsSuccess={IsSuccess}, ApiAccount={ApiAccount}",
            dto.IsSuccess, dto.ApiAccount?.APIAccount);

        return new CreateTeamApiAccountResult(
            IsSuccess:   dto.IsSuccess,
            Description: dto.Description ?? string.Empty,
            ErrorCode:   dto.ErrorCode,
            ApiAccount:  dto.ApiAccount?.APIAccount ?? string.Empty,
            ApiKey:      dto.ApiAccount?.APIKey     ?? string.Empty);
    }

    // ─────────────────────────────────────────────────────────────
    // §6.6.4 虛擬帳號發佈大團隊貼文（走 TeamService.ashx）
    // ─────────────────────────────────────────────────────────────
    public async Task<PostTeamMessageResult> PostTeamMessageAsync(
        string virtualAccount,
        string virtualApiKey,
        long teamSn,
        string textContent,
        string subject,
        CancellationToken ct = default)
    {
        // 注意：postMessage 走 TeamService.ashx，認證為虛擬帳號，
        // 不使用 BuildBaseForm（那是 system_sn + api_key）
        var teamServiceUrl = $"{_options.BaseUrl.TrimEnd('/')}/API/TeamService.ashx";

        var formData = new List<KeyValuePair<string, string>>
        {
            KV("ask",          "postMessage"),
            KV("account",      virtualAccount),
            KV("api_key",      virtualApiKey),
            KV("team_sn",      teamSn.ToString()),
            KV("content_type", "1"),
            KV("text_content", textContent),
            KV("subject",      subject)
        };

        var pipeline = _polly.GetPipeline(ResiliencePipelineKeys.TeamPlus);

        var raw = await pipeline.ExecuteAsync(async token =>
        {
            using var content  = new FormUrlEncodedContent(formData);
            using var response = await _http.PostAsync(teamServiceUrl, content, token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(token);
        }, ct);

        var dto = Deserialize<PostTeamMessageResponseDto>(raw, "postMessage");

        _logger.LogInformation(
            "postMessage 完成：IsSuccess={IsSuccess}, BatchId={BatchId}",
            dto.IsSuccess, dto.BatchId);

        return new PostTeamMessageResult(
            IsSuccess:   dto.IsSuccess,
            Description: dto.Description ?? string.Empty,
            ErrorCode:   dto.ErrorCode,
            BatchId:     dto.BatchId ?? string.Empty);
    }

    // ─── 私有 DTO（加入 TeamPlusSystemClient 底部私有 DTO 區段）──────

    private sealed record CreateTeamApiAccountResponseDto(
        bool IsSuccess,
        string? Description,
        int ErrorCode,
        ApiAccountDto? APIAccount);

    private sealed record ApiAccountDto(
        string APIAccount,
        string APIKey,
        string? Name,
        bool IsEnable);

    private sealed record PostTeamMessageResponseDto(
        bool IsSuccess,
        string? Description,
        int ErrorCode,
        string? BatchId);
*/
