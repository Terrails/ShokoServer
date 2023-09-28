using Shoko.Server.Filters.Interfaces;

namespace Shoko.Server.Filters.SortingSelectors;

public class LastAddedDateSortingSelector : SortingExpression
{
    public override bool TimeDependent => false;
    public override bool UserDependent => false;

    public override object Evaluate(IFilterable f)
    {
        return f.LastAddedDate;
    }
}