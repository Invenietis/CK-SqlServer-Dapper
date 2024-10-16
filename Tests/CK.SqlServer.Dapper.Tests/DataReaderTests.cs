using System.Collections.Generic;
using System.Linq;
using Xunit;
using Dapper;
using static CK.Testing.SqlServerTestHelper;

namespace CK.SqlServer.Dapper.Tests;

public class DataReaderTests : TestBase
{
    [Fact]
    public void DiscriminatedUnion()
    {
        List<Discriminated_BaseType> result = new List<Discriminated_BaseType>();
        using( var reader = controller.ExecuteReader( @"
select 'abc' as Name, 1 as Type, 3.0 as Value
union all
select 'def' as Name, 2 as Type, 4.0 as Value" ) )
        {
            if( reader.Read() )
            {
                var toFoo = reader.GetRowParser<Discriminated_BaseType>( typeof( Discriminated_Foo ) );
                var toBar = reader.GetRowParser<Discriminated_BaseType>( typeof( Discriminated_Bar ) );

                var col = reader.GetOrdinal( "Type" );
                do
                {
                    switch( reader.GetInt32( col ) )
                    {
                        case 1:
                            result.Add( toFoo( reader ) );
                            break;
                        case 2:
                            result.Add( toBar( reader ) );
                            break;
                    }
                } while( reader.Read() );
            }
        }

        Assert.Equal( 2, result.Count );
        Assert.Equal( 1, result[0].Type );
        Assert.Equal( 2, result[1].Type );
        var foo = (Discriminated_Foo)result[0];
        Assert.Equal( "abc", foo.Name );
        var bar = (Discriminated_Bar)result[1];
        Assert.Equal( (float)4.0, bar.Value );
    }

    [Fact]
    public void DiscriminatedUnionWithMultiMapping()
    {
        var result = new List<DiscriminatedWithMultiMapping_BaseType>();
        using( var reader = controller.ExecuteReader( @"
select 'abc' as Name, 1 as Type, 3.0 as Value, 1 as Id, 'zxc' as Name
union all
select 'def' as Name, 2 as Type, 4.0 as Value, 2 as Id, 'qwe' as Name" ) )
        {
            if( reader.Read() )
            {
                var col = reader.GetOrdinal( "Type" );
                var splitOn = reader.GetOrdinal( "Id" );

                var toFoo = reader.GetRowParser<DiscriminatedWithMultiMapping_BaseType>( typeof( DiscriminatedWithMultiMapping_Foo ), 0, splitOn );
                var toBar = reader.GetRowParser<DiscriminatedWithMultiMapping_BaseType>( typeof( DiscriminatedWithMultiMapping_Bar ), 0, splitOn );
                var toHaz = reader.GetRowParser<HazNameId>( typeof( HazNameId ), splitOn, reader.FieldCount - splitOn );

                do
                {
                    DiscriminatedWithMultiMapping_BaseType? obj = null;
                    switch( reader.GetInt32( col ) )
                    {
                        case 1:
                            obj = toFoo( reader );
                            break;
                        case 2:
                            obj = toBar( reader );
                            break;
                    }

                    Assert.NotNull( obj );
                    obj.HazNameIdObject = toHaz( reader );
                    result.Add( obj );

                } while( reader.Read() );
            }
        }

        Assert.Equal( 2, result.Count );
        Assert.Equal( 1, result[0].Type );
        Assert.Equal( 2, result[1].Type );
        var foo = (DiscriminatedWithMultiMapping_Foo)result[0];
        Assert.Equal( "abc", foo.Name );
        Assert.Equal( 1, foo.HazNameIdObject!.Id );
        Assert.Equal( "zxc", foo.HazNameIdObject.Name );
        var bar = (DiscriminatedWithMultiMapping_Bar)result[1];
        Assert.Equal( (float)4.0, bar.Value );
        Assert.Equal( 2, bar.HazNameIdObject!.Id );
        Assert.Equal( "qwe", bar.HazNameIdObject.Name );
    }

    private abstract class Discriminated_BaseType
    {
        public abstract int Type { get; }
    }

    private class Discriminated_Foo : Discriminated_BaseType
    {
        public string? Name { get; set; }
        public override int Type => 1;
    }

    private class Discriminated_Bar : Discriminated_BaseType
    {
        public float Value { get; set; }
        public override int Type => 2;
    }

    private abstract class DiscriminatedWithMultiMapping_BaseType : Discriminated_BaseType
    {
        public abstract HazNameId? HazNameIdObject { get; set; }
    }

    private class DiscriminatedWithMultiMapping_Foo : DiscriminatedWithMultiMapping_BaseType
    {
        public override HazNameId? HazNameIdObject { get; set; }
        public string? Name { get; set; }
        public override int Type => 1;
    }

    private class DiscriminatedWithMultiMapping_Bar : DiscriminatedWithMultiMapping_BaseType
    {
        public override HazNameId? HazNameIdObject { get; set; }
        public float Value { get; set; }
        public override int Type => 2;
    }
}
