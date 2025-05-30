using Shoko.Server.Filters.Interfaces;

namespace Shoko.Server.Filters.Info;

/// <summary>
///     Missing Links include logic for whether a link should exist
/// </summary>
public class MissingTraktLinkExpression : FilterExpression<bool>
{
    public override bool TimeDependent => false;
    public override bool UserDependent => false;
    public override string HelpDescription => "This condition passes if any of the anime should have a Trakt link but does not have one";

    public override bool Evaluate(IFilterable filterable, IFilterableUserInfo userInfo)
    {
        return !filterable.HasTraktLink;
    }

    protected bool Equals(MissingTraktLinkExpression other)
    {
        return base.Equals(other);
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj))
        {
            return false;
        }

        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        if (obj.GetType() != this.GetType())
        {
            return false;
        }

        return Equals((MissingTraktLinkExpression)obj);
    }

    public override int GetHashCode()
    {
        return GetType().FullName!.GetHashCode();
    }

    public static bool operator ==(MissingTraktLinkExpression left, MissingTraktLinkExpression right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(MissingTraktLinkExpression left, MissingTraktLinkExpression right)
    {
        return !Equals(left, right);
    }
}
