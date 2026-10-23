// The tests that switch the interface language change the shared Loc instance, and every test that checks an English text
// reads it, so the tests of this assembly share one collection and run one after the other.
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]
