data class User(val id: Int, val name: String)
fun main() { listOf(User(1, "a")).filter { it.id > 0 }.forEach(::println) }
