﻿namespace PRN231.ExploreNow.BusinessObject.Entities;

public class TourTimestamp : BaseEntity
{
    public string Title { get; set; }
    public string Description { get; set; }
    public Guid TourId { get; set; }
    public Tour Tour { get; set; }
}