// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;

namespace Menus.Abstract.Models
{
    public interface ITypoRequest
    {
        string Id { get; set; }
        string UserId { get; set; }
        string Text { get; set; }
        string Url { get; set; }
        string Email { get; set; }
        DateTime CreateDate { get; set; }
        TypoRequestStatus Status { get; set; }
    }

    public enum TypoRequestStatus
    {
        New,
        InProcess,
        Closed
    }
}
