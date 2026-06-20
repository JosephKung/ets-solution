// ======================================================================
// 將以下程式碼貼入 TeamPlusSystemClient.cs（同 1.3.10 的做法）
// 位置：PostTeamMessageAsync 結尾 } 之後、私有 DTO 區段之前
// ======================================================================

// ─────────────────────────────────────────────────────────────────────
// §9.2.2 批次解析 LoginName → UserNo（v3.2 新增）
// ─────────────────────────────────────────────────────────────────────
// public async Task<Dictionary<string, int>> GetUserNosAsync(
//     IReadOnlyList<string> loginNames,
//     CancellationToken ct = default)
// {
//     var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
//     var batchSize = 50;  // §9.2.2 單次最多 50 個
//
//     foreach (var batch in loginNames.Chunk(batchSize))
//     {
//         var uidListJson = JsonSerializer.Serialize(batch);
//         var form = new List<KeyValuePair<string, string>>
//         {
//             KV("ask",       "getUserInfoList"),
//             KV("system_sn", _options.SystemSn),
//             KV("api_key",   _options.ApiKey),
//             KV("root_code", string.Empty),
//             KV("uid_type",  "0"),
//             KV("uid_list",  uidListJson)
//         };
//
//         var pipeline = _polly.GetPipeline(ResiliencePipelineKeys.TeamPlus);
//         var url = $"{_options.BaseUrl.TrimEnd('/')}/API/SystemServiceExt01.ashx?ask=getUserInfoList";
//
//         var raw = await pipeline.ExecuteAsync(async token =>
//         {
//             using var content  = new FormUrlEncodedContent(form);
//             using var response = await _http.PostAsync(url, content, token);
//             response.EnsureSuccessStatusCode();
//             return await response.Content.ReadAsStringAsync(token);
//         }, ct);
//
//         var dto = Deserialize<GetUserInfoListResponseDto>(raw, "getUserInfoList");
//
//         if (!dto.IsSuccess)
//         {
//             _logger.LogWarning("getUserInfoList 失敗：{Desc}", dto.Description);
//             continue;  // Skip 模式（Halt 模式由 caller 判斷）
//         }
//
//         foreach (var u in dto.UserInfo ?? [])
//             result[u.LoginName] = u.UserNo;
//
//         // 找出未回傳的 LoginName，寫 Warning
//         var notFound = batch.Except(result.Keys, StringComparer.OrdinalIgnoreCase).ToList();
//         if (notFound.Count > 0)
//             _logger.LogWarning(
//                 "getUserInfoList 查無帳號（{Count} 人）：{List}",
//                 notFound.Count, string.Join(",", notFound));
//     }
//
//     return result;
// }
//
// ─── 私有 DTO（加入私有 DTO 區段）──────────────────────────────────────
// private sealed record GetUserInfoListResponseDto(
//     bool IsSuccess,
//     string? Description,
//     int ErrorCode,
//     UserInfoDto[]? UserInfo);
//
// private sealed record UserInfoDto(
//     int UserNo,
//     string LoginName,
//     string DeptCode,
//     string UserName,
//     string Lang,
//     int Status,
//     string MVPN);
