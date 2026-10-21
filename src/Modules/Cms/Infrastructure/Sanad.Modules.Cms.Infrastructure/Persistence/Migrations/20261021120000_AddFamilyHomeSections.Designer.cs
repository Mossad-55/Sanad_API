using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Sanad.Modules.Cms.Infrastructure.Persistence;
using System;

namespace Sanad.Modules.Cms.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(CmsDbContext))]
    [Migration("20261021120000_AddFamilyHomeSections")]
    partial class AddFamilyHomeSections
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
#pragma warning restore 612, 618
        }
    }
}