using System;
using Shoko.Server.Filters.Interfaces;

namespace Shoko.Server.Filters.SortingSelectors;

public class HighestUserRatingSortingSelector : UserDependentSortingExpression
{
    public override bool TimeDependent => false;
    public override bool UserDependent => true;

    public override object Evaluate(IUserDependentFilterable f)
    {
        return Convert.ToDouble(f.HighestUserRating);
    }
}