module Demo
  class Counter
    attr_reader :count
    def initialize = @count = 0
    def tick! = @count += 1
  end
end
puts Demo::Counter.new.tap(&:tick!).count
