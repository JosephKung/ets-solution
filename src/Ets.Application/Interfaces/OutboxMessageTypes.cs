// src/Ets.Application/Interfaces/OutboxMessageTypes.cs
// 注意：OutboxMessageType enum 定義於 Ets.Domain.Enums.OutboxMessageType
// 本檔案僅作為 enum 值說明的文件用途，實際使用請直接引用 Ets.Domain.Enums.OutboxMessageType
//
// OutboxMessageType enum 預期包含以下值（若 M1/M2 尚未加入，需補上）：
//   CreateTeam            = 0
//   CreateChat            = 1
//   InviteTeamMember      = 2
//   InviteChatMember      = 3
//   AssignManager         = 4
//   SendFlexMessage       = 5
//   PostVirtualMsg        = 6
//   CreateTeamAPIAccount  = 7
