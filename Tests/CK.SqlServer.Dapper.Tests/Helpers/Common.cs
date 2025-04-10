using System;
using System.Data;
using System.Data.Common;
using Xunit;
using Dapper;

namespace CK.SqlServer.Dapper.Tests;

public static class Common
{
    public static Type GetSomeType() => typeof( SomeType );

    public static void DapperEnumValue( IDbConnection connection )
    {
        // test passing as AsEnum, reading as int
        var v = (AnEnum)connection.QuerySingle<int>( "select @v, @y, @z", new { v = AnEnum.B, y = (AnEnum?)AnEnum.B, z = (AnEnum?)null } );
        Assert.Equal( AnEnum.B, v );

        var args = new DynamicParameters();
        args.Add( "v", AnEnum.B );
        args.Add( "y", AnEnum.B );
        args.Add( "z", null );
        v = (AnEnum)connection.QuerySingle<int>( "select @v, @y, @z", args );
        Assert.Equal( AnEnum.B, v );

        // test passing as int, reading as AnEnum
        var k = (int)connection.QuerySingle<AnEnum>( "select @v, @y, @z", new { v = (int)AnEnum.B, y = (int?)(int)AnEnum.B, z = (int?)null } );
        Assert.Equal( (int)AnEnum.B, k );

        args = new DynamicParameters();
        args.Add( "v", (int)AnEnum.B );
        args.Add( "y", (int)AnEnum.B );
        args.Add( "z", null );
        k = (int)connection.QuerySingle<AnEnum>( "select @v, @y, @z", args );
        Assert.Equal( (int)AnEnum.B, k );
    }

    public static void TestDateTime( ISqlConnectionController controller )
    {
        DateTime? now = DateTime.UtcNow;
        try { controller.Execute( "DROP TABLE Persons" ); } catch { /* don't care */ }
        controller.Execute( "CREATE TABLE Persons (id int not null, dob datetime null)" );
        controller.Execute( "INSERT Persons (id, dob) values (@id, @dob)",
             new { id = 7, dob = (DateTime?)null } );
        controller.Execute( "INSERT Persons (id, dob) values (@id, @dob)",
             new { id = 42, dob = now } );

        var row = controller.QueryFirstOrDefault<NullableDatePerson>(
            "SELECT id, dob, dob as dob2 FROM Persons WHERE id=@id", new { id = 7 } );
        Assert.NotNull( row );
        Assert.Equal( 7, row.Id );
        Assert.Null( row.DoB );
        Assert.Null( row.DoB2 );

        row = controller.QueryFirstOrDefault<NullableDatePerson>(
            "SELECT id, dob FROM Persons WHERE id=@id", new { id = 42 } );
        Assert.NotNull( row );
        Assert.Equal( 42, row.Id );
        row.DoB.Equals( now );
        row.DoB2.Equals( now );
    }

    private class NullableDatePerson
    {
        public int Id { get; set; }
        public DateTime? DoB { get; set; }
        public DateTime? DoB2 { get; set; }
    }
}
