use std::collections::HashMap;
fn main() {
    let mut m: HashMap<&str, i32> = HashMap::new();
    *m.entry("a").or_insert(0) += 1;
    println!("{:?}", m);
}
