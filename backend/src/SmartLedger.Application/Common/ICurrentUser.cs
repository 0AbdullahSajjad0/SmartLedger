namespace SmartLedger.Application.Common;

public interface ICurrentUser
{
    Guid? UserId { get; }
}
