package demo;
import java.util.List;
public class Main {
    public static void main(String[] args) {
        List<String> names = List.of("a", "b");
        names.forEach(System.out::println);
    }
}
